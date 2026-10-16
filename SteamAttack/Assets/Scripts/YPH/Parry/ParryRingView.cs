using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 플레이어의 ParryHUD 캔버스에서 대상별 동심원을 표시합니다. ParryController와 그림·미리 만든 원을 연결합니다.
    /// 공격 예고가 오면 빈 원을 빌리고, 입력 결과나 대상 종료에 맞춰 숨겨 다시 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(AudioSource))]
    public class ParryRingView : MonoBehaviour
    {
        private const float SuccessDuration = 0.25f;

        [Header("연결")]
        [SerializeField, Tooltip("이 HUD를 가진 플레이어의 받아치기입니다. 비우면 판정 폭과 결과를 알 수 없어 표시를 끕니다.")]
        private ParryController _parry;
        [SerializeField, Tooltip("바깥 원의 검정 먹선 Sprite입니다. YPH 폴더의 복사본을 연결하며 비우면 표시를 끕니다.")]
        private Sprite _blackRing;
        [SerializeField, Tooltip("안쪽 원의 밝은 먹선 Sprite입니다. 비우면 검정 먹선을 대신 사용합니다.")]
        private Sprite _whiteRing;
        [SerializeField, Tooltip("판정 구간과 성공 때 사용할 빨간 먹선 Sprite입니다. 비우면 표시를 끕니다.")]
        private Sprite _redRing;
        [SerializeField, Tooltip("프리팹에 미리 만든 원의 루트입니다. 각 루트에는 CanvasGroup과 Inner·Outer Image가 필요합니다. 개수를 늘리려면 프리팹에서 원도 복제해 연결하세요.")]
        private RectTransform[] _ringRoots;

        [Header("모양·움직임")]
        [SerializeField, Min(10f), Tooltip("1920×1080 기준 안쪽 원 지름(px)입니다. 높이면 원이 크게 보입니다.")]
        private float _ringSize = 120f;
        [SerializeField, Range(0f, 1f), Tooltip("안쪽 원의 불투명도입니다. 1이면 진하고 0이면 보이지 않습니다.")]
        private float _innerAlpha = 0.88f;
        [SerializeField, Min(0.01f), Tooltip("겹침 전 바깥 원이 줄어드는 시간(초)입니다. 높이면 천천히 줄어들며, 짧은 예고는 진행된 크기에서 시작합니다.")]
        private float _shrinkTime = 0.8f;
        [SerializeField, Min(1f), Tooltip("바깥 원의 시작 크기 배율입니다. 2면 안쪽 원의 두 배이며, 1이면 크기가 줄어들지 않습니다.")]
        private float _outerStartScale = 2f;
        [SerializeField, Tooltip("줄어드는 동안 시계 방향으로 도는 각도(도)입니다. 0이면 회전하지 않고 음수면 반대로 돕니다.")]
        private float _outerRotation = 135f;
        [SerializeField, Range(1f, 5f), Tooltip("회전 가속 정도입니다. 1이면 일정한 속도, 높이면 겹치기 직전에 빠르게 돕니다.")]
        private float _rotationEaseIn = 3f;
        [SerializeField, Min(1), Tooltip("동시에 표시할 원의 최대 개수입니다. 시작할 때 연결된 원 중 이 개수만 사용합니다. 늘리려면 Ring Roots에도 같은 수의 원이 있어야 합니다.")]
        private int _maxRings = 4;

        [Header("소리 — 비우면 소리 없음")]
        [SerializeField, Tooltip("붉은 판정 구간에 들어올 때 한 번 재생할 효과음입니다. 비우면 재생하지 않습니다.")]
        private AudioClip _overlapSfx;
        [SerializeField, Tooltip("이 플레이어가 받아치기에 성공했을 때 재생할 효과음입니다. 비우면 재생하지 않습니다.")]
        private AudioClip _successSfx;
        [SerializeField, Range(0f, 1f), Tooltip("효과음 크기입니다. 높이면 커지고 0이면 소리가 나지 않습니다.")]
        private float _sfxVolume = 0.8f;

        // 참조와 표시 진행도만 저장합니다. 원 오브젝트는 프리팹에 있어 공격할 때 생성하지 않습니다.
        private sealed class Ring
        {
            public RectTransform Root;
            public UnityEngine.UI.Image Inner;
            public UnityEngine.UI.Image Outer;
            public CanvasGroup Group;
            public IParryTarget Target;
            public float SuccessUntil;
            public bool OverlapPlayed;
        }

        private Ring[] _rings;
        private Camera _camera;
        private AudioSource _audio;

        /// <summary>프리팹의 원을 한 번 찾아 캐시합니다. 부족한 설정은 자동 생성으로 숨기지 않고 오류로 알립니다.</summary>
        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (_parry == null || _blackRing == null || _redRing == null || _ringRoots == null || _maxRings < 1 || _ringRoots.Length < _maxRings)
            {
                Debug.LogError("[ParryRingView] 플레이어·먹선 그림·최대 개수만큼의 Ring Roots를 연결해야 합니다.", this);
                enabled = false;
                return;
            }
            _rings = new Ring[_maxRings];
            for (int i = 0; i < _rings.Length; i++)
            {
                RectTransform root = _ringRoots[i];
                Transform inner = root != null ? root.Find("Inner") : null;
                Transform outer = root != null ? root.Find("Outer") : null;
                var ring = new Ring
                {
                    Root = root,
                    Group = root != null ? root.GetComponent<CanvasGroup>() : null,
                    Inner = inner != null ? inner.GetComponent<UnityEngine.UI.Image>() : null,
                    Outer = outer != null ? outer.GetComponent<UnityEngine.UI.Image>() : null
                };
                _rings[i] = ring;
                if (ring.Group == null || ring.Inner == null || ring.Outer == null)
                {
                    Debug.LogError("[ParryRingView] 원에 CanvasGroup과 Inner·Outer Image가 필요합니다.", this);
                    enabled = false;
                    return;
                }
                Release(ring);
            }
        }

        /// <summary>활성화 때 카메라를 한 번 찾고 이 플레이어의 결과만 구독합니다.</summary>
        private void OnEnable()
        {
            _camera = Camera.main;
            ParryTargets.AttackStarted += OnAttackStarted;
            _parry.ParryAttempted += OnParryAttempted;
            _parry.TargetParried += OnTargetParried;
        }

        /// <summary>카메라 이동이 끝난 뒤 원을 대상에 맞춥니다. 파괴·비활성·공격 종료는 원 반환으로 처리합니다.</summary>
        private void LateUpdate()
        {
            // 카메라가 HUD보다 늦게 켜지거나 기존 카메라가 파괴되면 새 참조를 기다립니다.
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            for (int i = 0; i < _rings.Length; i++)
            {
                Ring ring = _rings[i];
                IParryTarget target = ring.Target;
                if (target == null)
                {
                    continue;
                }
                if ((target as Object) == null || (target is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                    || (ring.SuccessUntil <= 0f && !target.IsParryWindowOpen && !target.IsParryStunned))
                {
                    Release(ring);
                    continue;
                }
                float alpha = UpdateAppearance(ring);
                if (ring.Target != null)
                {
                    Vector3 screen = _camera != null ? _camera.WorldToScreenPoint(target.ParryPoint) : Vector3.back;
                    ring.Root.position = new Vector3(screen.x, screen.y, 0f);
                    ring.Group.alpha = screen.z > 0f ? alpha : 0f;
                }
            }
        }

        /// <summary>재활성화·다음 Play에 원이나 구독이 남지 않게 정리합니다.</summary>
        private void OnDisable()
        {
            ParryTargets.AttackStarted -= OnAttackStarted;
            if (_parry != null)
            {
                _parry.ParryAttempted -= OnParryAttempted;
                _parry.TargetParried -= OnTargetParried;
            }
            HideAll();
        }

        /// <summary>같은 대상의 원을 재설정하거나 빈 원 하나를 빌립니다. 풀이 가득 차면 새 원은 생략합니다.</summary>
        private void OnAttackStarted(IParryTarget target, float secondsUntilOverlap)
        {
            if ((target as Object) == null || !target.IsParryWindowOpen)
            {
                return;
            }
            Ring available = null;
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Target == target)
                {
                    available = _rings[i];
                    break;
                }
                if (available == null && _rings[i].Target == null)
                {
                    available = _rings[i];
                }
            }
            if (available == null)
            {
                return;
            }
            available.Target = target;
            available.SuccessUntil = 0f;
            available.OverlapPlayed = false;
            available.Root.localScale = Vector3.one;
            available.Root.sizeDelta = Vector2.one * _ringSize;
            available.Group.alpha = 0f;
            available.Root.gameObject.SetActive(true);
        }

        /// <summary>실패한 시도는 쿨타임에 들어가므로 화면에 남은 예고도 함께 지웁니다.</summary>
        private void OnParryAttempted(bool success)
        {
            if (!success)
            {
                HideAll();
            }
        }

        /// <summary>성공한 대상의 원만 빨갛게 번졌다 사라지게 합니다. 대상이 화면에 없어도 성공 소리는 한 번 냅니다.</summary>
        private void OnTargetParried(IParryTarget target)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Target == target)
                {
                    _rings[i].SuccessUntil = Time.time + SuccessDuration;
                    break;
                }
            }
            PlaySfx(_successSfx);
        }

        /// <summary>겹침 시각을 매번 읽어 히트스톱으로 늦어진 예고도 맞춥니다. 반환값은 원의 불투명도입니다.</summary>
        private float UpdateAppearance(Ring ring)
        {
            if (ring.SuccessUntil > 0f)
            {
                float remaining = (ring.SuccessUntil - Time.time) / SuccessDuration;
                if (remaining <= 0f)
                {
                    Release(ring);
                    return 0f;
                }
                SetRed(ring, true);
                ring.Outer.rectTransform.localScale = Vector3.one;
                ring.Root.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, remaining);
                return remaining;
            }

            float untilOverlap = ring.Target.ParryOverlapTime - Time.time;
            float progress = Mathf.Clamp01(1f - untilOverlap / Mathf.Max(0.01f, _shrinkTime));
            ring.Outer.rectTransform.localScale = Vector3.one * Mathf.Lerp(_outerStartScale, 1f, progress);
            ring.Outer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_outerRotation * Mathf.Pow(progress, _rotationEaseIn));
            bool canParry = ring.Target.IsParryWindowOpen && Mathf.Abs(untilOverlap) <= _parry.JudgeHalfWindow;
            SetRed(ring, canParry);
            if (canParry && !ring.OverlapPlayed)
            {
                ring.OverlapPlayed = true;
                PlaySfx(_overlapSfx);
            }
            return 1f;
        }

        /// <summary>판정 폭은 Controller 한 곳에서 읽고, 먹선 그림만 전환합니다.</summary>
        private void SetRed(Ring ring, bool red)
        {
            ring.Inner.sprite = red ? _redRing : (_whiteRing != null ? _whiteRing : _blackRing);
            ring.Outer.sprite = red ? _redRing : _blackRing;
            ring.Inner.color = new Color(1f, 1f, 1f, _innerAlpha);
        }

        /// <summary>소리 파일이 있을 때만 같은 AudioSource로 재생합니다.</summary>
        private void PlaySfx(AudioClip clip)
        {
            if (clip != null)
            {
                _audio.PlayOneShot(clip, _sfxVolume);
            }
        }

        /// <summary>실패·비활성화 시 사용 중인 원을 모두 반환합니다. 초기화 실패 중에도 안전하게 정리합니다.</summary>
        private void HideAll()
        {
            if (_rings == null)
            {
                return;
            }
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i] != null && _rings[i].Root != null)
                {
                    Release(_rings[i]);
                }
            }
        }

        /// <summary>오브젝트를 파괴하지 않고 연결과 성공 진행도만 비워 다음 공격에 재사용합니다.</summary>
        private void Release(Ring ring)
        {
            ring.Target = null;
            ring.SuccessUntil = 0f;
            ring.Root.gameObject.SetActive(false);
        }
    }
}
