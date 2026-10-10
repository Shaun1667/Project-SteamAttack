using UnityEngine;
using UnityEngine.UI;
using NGH;

namespace CHG
{
    /// <summary>
    /// 일반 몬스터 체력바 (개체별로 따로 관리, 피해 수치는 표시하지 않음)
    /// - 켜짐  : 플레이어가 이 몬스터에게 피해를 입히면(근접·화승총·수류탄) 또는 패링에 성공하면 거리와 관계없이 켜진다.
    ///           락온만으로는 켜지지 않는다.
    /// - 유지  : 이 몬스터가 플레이어를 추적하는 동안 계속 표시한다.
    /// - 숨김  : 카메라 화면 밖이거나 장애물에 가려지면 잠시 숨기고, 다시 보이면 표시한다.
    ///           (다른 몬스터·플레이어는 장애물로 보지 않음. 짧게 스치는 가려짐은 무시)
    /// - 꺼짐  : 추적이 끝나거나 죽으면 꺼진다. 다시 보려면 다시 피해를 입혀야 한다.
    /// - 부활하거나 풀에서 다시 꺼내지면 꺼진 상태로 시작한다.
    /// - 보스는 상단 고정 체력바를 쓰므로 이 컴포넌트를 붙이지 않는다.
    /// - 개발용 표시(0 키)를 켜면 체력바 오른쪽에 '현재 / 최대' 수치를 띄운다. (실제 플레이에서는 수치 없음)
    ///
    /// 체력바 UI는 화면 고정 크기(Screen Space Overlay)이며, 오브젝트 풀(ObjectPool)에서 꺼내 쓰고 반납한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public class EnemyHpBar : MonoBehaviour, IPoolable
    {
        [Header("위치 / 크기")]
        [Tooltip("머리 뼈(Head) 위로 띄우는 높이(m). 머리 뼈가 없으면 몸 콜라이더 꼭대기 기준")]
        [SerializeField] private float heightOffset = 0.2f;
        [Tooltip("Humanoid면 머리 뼈를 따라감 (숙이거나 쓰러지는 동작에도 머리 위에 붙음)")]
        [SerializeField] private bool followHeadBone = true;
        [Tooltip("머리를 따라갈 때 흔들림을 줄이는 정도 (클수록 빠르게 따라감)")]
        [SerializeField, Min(1f)] private float followSharpness = 12f;
        [Tooltip("체력바 크기 (1920x1080 화면 기준 픽셀)")]
        [SerializeField] private Vector2 barSize = new Vector2(110f, 9f);

        [Header("색")]
        [SerializeField] private Color fillColor = new Color(0.72f, 0.13f, 0.11f, 1f);
        [Tooltip("깎인 부분이 늦게 줄어드는 잔상 색")]
        [SerializeField] private Color trailColor = new Color(0.95f, 0.92f, 0.85f, 1f);
        [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("연출")]
        [Tooltip("켜지고 꺼질 때 페이드 시간(초)")]
        [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;
        [Tooltip("맞은 뒤 잔상이 줄어들기 시작할 때까지 시간(초)")]
        [SerializeField, Min(0f)] private float trailDelay = 0.35f;
        [Tooltip("잔상이 줄어드는 속도 (초당 체력바 전체 대비 비율)")]
        [SerializeField, Min(0.01f)] private float trailSpeed = 0.8f;

        [Header("가려짐")]
        [SerializeField] private bool hideWhenOccluded = true;
        [Tooltip("장애물로 볼 레이어")]
        [SerializeField] private LayerMask occlusionMask = ~0;
        [Tooltip("이 시간(초) 이상 계속 가려져야 숨김 (기둥을 스칠 때 깜빡임 방지)")]
        [SerializeField, Min(0f)] private float occlusionHideDelay = 0.2f;
        [Tooltip("가려짐 검사 간격(초)")]
        [SerializeField, Min(0.02f)] private float occlusionCheckInterval = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logState = false;

        /// <summary>전투 중이라 체력바를 보여 줘야 하는 상태인지 (화면 밖·가려짐으로 잠시 숨은 경우도 true)</summary>
        public bool IsActive => combatActive;

        private const string CanvasName = "chg_EnemyHpBarCanvas";
        private const string BarName = "chg_EnemyHpBar";
        private static GameObject canvasTemplate;
        private static GameObject barTemplate;
        private static RectTransform canvasRoot;
        private static readonly RaycastHit[] rayHits = new RaycastHit[16];

        private EnemyHealth health;
        private EnemyAI ai;
        private float topHeight = 1.8f;
        private Transform headBone;
        private Vector3 smoothedAnchor;
        private bool hasSmoothedAnchor;

        private bool combatActive;
        private RectTransform bar;
        private CanvasGroup group;
        private RectTransform fillRect;
        private RectTransform trailRect;
        private Text valueText;
        private int shownHp = -1;
        private bool shownValue;
        private float alpha;
        private float trailRatio = 1f;
        private float trailHoldTimer;
        private bool occluded;
        private float occludedTime;
        private float nextOcclusionCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            canvasTemplate = null;
            barTemplate = null;
            canvasRoot = null;
        }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            ai = GetComponent<EnemyAI>();

            health.Damaged += OnDamaged;
            health.Died += OnDied;
            health.Revived += ResetBar;
            if (ai != null)
            {
                ai.AwarenessChanged += OnAwarenessChanged;
                ai.Parried += Activate;
            }
        }

        private void Start()
        {
            // 머리 뼈 (Humanoid만)
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            }

            // 몸 꼭대기 높이는 처음 한 번만 잰다 (죽으면 콜라이더가 꺼지므로)
            Collider body = GetComponent<Collider>();
            if (body is CapsuleCollider capsule)
            {
                topHeight = (capsule.center.y + capsule.height * 0.5f) * transform.lossyScale.y;
            }
            else if (body != null && body.enabled && body.bounds.size.y > 0.01f)
            {
                topHeight = body.bounds.max.y - transform.position.y;
            }
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
                health.Revived -= ResetBar;
            }
            if (ai != null)
            {
                ai.AwarenessChanged -= OnAwarenessChanged;
                ai.Parried -= Activate;
            }
            ReleaseBar();
        }

        private void OnDisable()
        {
            // 몬스터가 꺼지면 체력바도 바로 반납
            combatActive = false;
            ReleaseBar();
        }

        public void OnSpawned()
        {
            ResetBar();
        }

        public void OnDespawned()
        {
            ResetBar();
        }

    #region 켜기 / 끄기

        private void OnDamaged(float amount, Vector3 hitFrom)
        {
            Activate();
        }

        private void OnDied()
        {
            Deactivate();
        }

        private void OnAwarenessChanged(bool aware)
        {
            if (!aware)
            {
                Deactivate();
            }
        }

        /// <summary>체력바를 켠다 (피해·패링 성공 시 자동 호출, 테스트 키 Alt+6)</summary>
        public void Activate()
        {
            if (health == null || !health.IsAlive || !isActiveAndEnabled)
            {
                return;
            }
            if (!combatActive && logState)
            {
                Debug.Log("[chg_EnemyHpBar:" + name + "] 체력바 켜짐", this);
            }
            combatActive = true;
            if (bar == null)
            {
                AcquireBar();
            }
        }

        /// <summary>체력바를 끈다 (페이드 후 반납). 다시 켜려면 피해를 입혀야 한다.</summary>
        public void Deactivate()
        {
            if (combatActive && logState)
            {
                Debug.Log("[chg_EnemyHpBar:" + name + "] 체력바 꺼짐", this);
            }
            combatActive = false;
        }

        private void ResetBar()
        {
            combatActive = false;
            ReleaseBar();
        }

    #endregion

    #region 매 프레임

        private void LateUpdate()
        {
            if (bar == null)
            {
                return;
            }

            float ratio = health.MaxHp > 0f ? Mathf.Clamp01(health.CurrentHp / health.MaxHp) : 0f;
            UpdateTrail(ratio);
            SetRatio(fillRect, ratio);
            SetRatio(trailRect, trailRatio);
            UpdateValueText();

            bool onScreen = false;
            Camera cam = Camera.main;
            Vector3 anchor = GetAnchor();
            if (cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint(anchor);
                onScreen = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
                if (onScreen)
                {
                    bar.position = new Vector3(screen.x, screen.y, 0f);
                    UpdateOcclusion(cam, anchor);
                }
            }

            bool show = combatActive && onScreen && !(hideWhenOccluded && occluded) && health.IsAlive;
            if (!onScreen)
            {
                alpha = 0f;   // 화면 밖은 바로 숨김
            }
            else
            {
                alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.unscaledDeltaTime / fadeTime);
            }
            group.alpha = alpha;

            // 전투가 끝나고 완전히 사라지면 풀로 반납
            if (!combatActive && alpha <= 0f)
            {
                ReleaseBar();
            }
        }

        // 체력바가 붙을 월드 위치: 머리 뼈 위 heightOffset (부드럽게 따라감), 없으면 콜라이더 꼭대기 위
        private Vector3 GetAnchor()
        {
            if (!followHeadBone || headBone == null)
            {
                return transform.position + Vector3.up * (topHeight + heightOffset);
            }

            Vector3 target = headBone.position + Vector3.up * heightOffset;
            if (!hasSmoothedAnchor)
            {
                smoothedAnchor = target;
                hasSmoothedAnchor = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
                smoothedAnchor = Vector3.Lerp(smoothedAnchor, target, t);
            }
            return smoothedAnchor;
        }

        private void UpdateTrail(float ratio)
        {
            if (ratio >= trailRatio)
            {
                trailRatio = ratio;
                trailHoldTimer = 0f;
                return;
            }

            if (trailHoldTimer < trailDelay)
            {
                trailHoldTimer += Time.deltaTime;
                return;
            }
            trailRatio = Mathf.MoveTowards(trailRatio, ratio, trailSpeed * Time.deltaTime);
            if (trailRatio <= ratio)
            {
                trailHoldTimer = 0f;
            }
        }

        private void UpdateOcclusion(Camera cam, Vector3 anchor)
        {
            if (!hideWhenOccluded)
            {
                occluded = false;
                return;
            }

            if (Time.unscaledTime >= nextOcclusionCheck)
            {
                nextOcclusionCheck = Time.unscaledTime + occlusionCheckInterval;
                bool blocked = IsBlocked(cam.transform.position, anchor);
                occludedTime = blocked ? occludedTime + occlusionCheckInterval : 0f;
            }
            occluded = occludedTime >= occlusionHideDelay && occludedTime > 0f;
        }

        // 카메라 → 머리 위 사이에 장애물이 있는지 (이 몬스터, 다른 몬스터, 플레이어는 무시)
        private bool IsBlocked(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.01f)
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(from, dir / distance, rayHits, distance, occlusionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = rayHits[i].collider;
                if (col == null || col.transform.IsChildOf(transform))
                {
                    continue;
                }
                if (col.GetComponentInParent<EnemyHealth>() != null)
                {
                    continue;
                }
                if (col.CompareTag("Player") || col.GetComponentInParent<PlayerController>() != null)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private static void SetRatio(RectTransform rect, float ratio)
        {
            if (rect != null)
            {
                rect.anchorMax = new Vector2(ratio, 1f);
                bool visible = ratio > 0.001f;
                if (rect.gameObject.activeSelf != visible)
                {
                    rect.gameObject.SetActive(visible);
                }
            }
        }

    #endregion

    #region UI (오브젝트 풀)

        private void AcquireBar()
        {
            RectTransform root = GetCanvasRoot();
            if (root == null)
            {
                return;
            }

            GameObject go = ObjectPool.Spawn(GetBarTemplate(), Vector3.zero, Quaternion.identity, root);
            if (go == null)
            {
                return;
            }

            bar = (RectTransform)go.transform;
            bar.localScale = Vector3.one;
            bar.sizeDelta = barSize;
            group = go.GetComponent<CanvasGroup>();
            trailRect = (RectTransform)bar.Find("Trail");
            Transform value = bar.Find("Value");
            valueText = value != null ? value.GetComponent<Text>() : null;
            shownHp = -1;
            shownValue = false;
            if (valueText != null) valueText.gameObject.SetActive(false);
            fillRect = (RectTransform)bar.Find("Fill");
            bar.Find("Back").GetComponent<Image>().color = backColor;
            trailRect.GetComponent<Image>().color = trailColor;
            fillRect.GetComponent<Image>().color = fillColor;

            float ratio = health.MaxHp > 0f ? Mathf.Clamp01(health.CurrentHp / health.MaxHp) : 0f;
            // 처음 맞은 피해도 잔상으로 보이도록 직전 HP에서 시작
            trailRatio = Mathf.Max(ratio, trailRatio);
            trailHoldTimer = 0f;
            alpha = 0f;
            group.alpha = 0f;
            hasSmoothedAnchor = false;
            occluded = false;
            occludedTime = 0f;
            nextOcclusionCheck = 0f;
        }

        private void ReleaseBar()
        {
            if (bar != null)
            {
                ObjectPool.Despawn(bar.gameObject);
            }
            bar = null;
            group = null;
            fillRect = null;
            valueText = null;
            trailRect = null;
            alpha = 0f;
            trailRatio = health != null && health.MaxHp > 0f ? Mathf.Clamp01(health.CurrentHp / health.MaxHp) : 1f;
        }

        // 모든 몬스터가 함께 쓰는 체력바 캔버스 (HUD보다 아래에 그림)
        private static RectTransform GetCanvasRoot()
        {
            if (canvasRoot != null && canvasRoot.gameObject.activeInHierarchy)
            {
                return canvasRoot;
            }

            if (canvasTemplate == null)
            {
                canvasTemplate = CreateTemplateHolder(CanvasName);
                Canvas canvas = canvasTemplate.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -50;
                CanvasScaler scaler = canvasTemplate.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            GameObject go = ObjectPool.Spawn(canvasTemplate, Vector3.zero, Quaternion.identity);
            canvasRoot = go != null ? (RectTransform)go.transform : null;
            return canvasRoot;
        }

        private static GameObject GetBarTemplate()
        {
            if (barTemplate != null)
            {
                return barTemplate;
            }

            barTemplate = CreateTemplateHolder(BarName);
            RectTransform root = barTemplate.AddComponent<RectTransform>();
            root.sizeDelta = new Vector2(110f, 9f);
            root.pivot = new Vector2(0.5f, 0.5f);
            CanvasGroup group = barTemplate.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            CreateImage(root, "Back", 0f);
            CreateImage(root, "Trail", 1f);
            CreateImage(root, "Fill", 1f);
            CreateValueText(root);
            return barTemplate;
        }

        // 템플릿은 비활성 상태로 풀 아래에 보관 (풀이 이걸 복제해서 씀)
        private static GameObject CreateTemplateHolder(string templateName)
        {
            GameObject go = new GameObject(templateName);
            go.SetActive(false);
            ObjectPool pool = ObjectPool.Instance;
            if (pool != null)
            {
                go.transform.SetParent(pool.transform, false);
            }
            return go;
        }

        // 개발용 표시(0 키)일 때만 '현재 / 최대' 수치
        private void UpdateValueText()
        {
            if (valueText == null)
            {
                return;
            }
            bool show = DebugView.Enabled;
            if (show != shownValue)
            {
                valueText.gameObject.SetActive(show);
                shownValue = show;
                shownHp = -1;
            }
            if (!show)
            {
                return;
            }
            int hp = Mathf.CeilToInt(health.CurrentHp);
            if (hp != shownHp)
            {
                valueText.text = hp + " / " + Mathf.CeilToInt(health.MaxHp);
                shownHp = hp;
            }
        }

        private static void CreateValueText(RectTransform parent)
        {
            GameObject go = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(Outline));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(6f, 0f);
            rect.sizeDelta = new Vector2(90f, 20f);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            go.SetActive(false);
        }

        private static void CreateImage(RectTransform parent, string childName, float inset)
        {
            GameObject go = new GameObject(childName, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
        }

    #endregion
    }
}
