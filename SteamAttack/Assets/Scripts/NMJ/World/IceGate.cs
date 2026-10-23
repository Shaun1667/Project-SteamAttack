using System.Collections.Generic;
using UnityEngine;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 얼음 문 — 광화문 가운데 홍예를 얼음으로 막아 두고, 지키는 적이 모두 쓰러지면 깨진다 (기획안 v4 4-2).
    ///
    /// - 지키는 적: Guards에 넣거나, 비워 두면 시작할 때 반경 Auto Find Radius 안의 적(YPH EnemyHealth)을 모은다.
    /// - 지키는 적이 하나도 없으면(아직 적을 배치하기 전) 처음부터 열어 둔다 → 테스트를 막지 않는다.
    /// - 깨질 때: 얼음 조각이 흩어지며 사라지고, "한 호흡에, 한 점에" 자막.
    /// - 얼음 모양은 임시(서리색 반투명 조각). 아트의 얼음 문(PR_IceGate)이 오면 Visual에 넣으면 그것을 쓴다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class IceGate : MonoBehaviour
    {
        [Header("지키는 적")]
        [SerializeField] private List<EnemyHealth> guards = new List<EnemyHealth>();
        [Tooltip("Guards가 비었을 때 이 반경 안의 적을 지키는 적으로 삼는다")]
        [SerializeField, Min(0f)] private float autoFindRadius = 25f;
        [Tooltip("지키는 적이 없으면 처음부터 열어 둔다 (적 배치 전 테스트용)")]
        [SerializeField] private bool openIfNoGuards = true;

        [Header("모양")]
        [Tooltip("얼음 문 모델. 비우면 서리색 조각으로 임시 모양을 만든다")]
        [SerializeField] private GameObject visual;
        [SerializeField] private Color iceColor = new Color(0.62f, 0.84f, 0.92f, 0.82f);   // 서리 #D4ECF1에 빙청 #7FD6E8을 섞어 눈밭 위에서도 보이게
        [SerializeField] private string caption = "한 호흡에, 한 점에";

        [Header("퀘스트")]
        [Tooltip("깨질 때 보고할 Custom 목표 아이디 (비우면 보고 안 함). 예: gate_gwanghwamun")]
        [SerializeField] private string questId = "";

        public bool IsOpen { get; private set; }

        private BoxCollider block;
        private readonly List<Transform> shards = new List<Transform>();
        private readonly List<Vector3> shardVel = new List<Vector3>();
        private Material iceMat;
        private float breakTimer = -1f;
        private float checkTimer;
        private bool lastHintShown;

        [Tooltip("근접 적이 한 명만 남았을 때 한 번 띄우는 안내 (비우면 안 띄움). 받아치기 → 경직 → 강공격을 보여 주려고")]
        [SerializeField] private string lastGuardHint = "마지막 한 명 — 내려치기를 받아치고(Ctrl) 경직된 틈에 강공격";

        private void Awake()
        {
            block = GetComponent<BoxCollider>();
            if (visual == null) BuildPlaceholder();
        }

        private void Start()
        {
            if (guards.Count == 0 && autoFindRadius > 0f)
                foreach (EnemyHealth e in FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude))
                    if (Vector3.Distance(e.transform.position, transform.position) <= autoFindRadius) guards.Add(e);

            if (guards.Count == 0 && openIfNoGuards)
            {
                Debug.Log($"[NMJ] 얼음 문 '{name}': 지키는 적이 없어 열어 둡니다 (적을 배치하면 닫힌 채 시작).", this);
                SetOpenImmediate();
            }
        }

        private void Update()
        {
            if (breakTimer >= 0f) { UpdateBreak(); return; }
            if (IsOpen || guards.Count == 0) return;   // 지키는 적이 없으면 저절로 열리지 않는다 (막아 두는 얼음벽)
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 0.25f;
            EnemyHealth lastAlive = null;
            int alive = 0;
            foreach (EnemyHealth g in guards) if (g != null && g.IsAlive) { alive++; lastAlive = g; }
            if (alive == 1 && !lastHintShown && !string.IsNullOrEmpty(lastGuardHint) && lastAlive.GetComponent<EnemyMeleeAttack>() != null)
            {
                lastHintShown = true;
                NmjSubtitle.Caption(lastGuardHint, 4f);
            }
            if (alive > 0) return;
            Break();
        }

        /// <summary>얼음 문을 깬다 (연출 + 자막)</summary>
        [ContextMenu("깨기")]
        public void Break()
        {
            if (IsOpen) return;
            IsOpen = true;
            block.enabled = false;
            breakTimer = 0f;
            if (visual != null) visual.SetActive(false);
            if (!string.IsNullOrEmpty(caption)) NmjSubtitle.Caption(caption, 3.5f);
            if (!string.IsNullOrEmpty(questId) && QuestManager.Instance != null) QuestManager.Instance.ReportCustom(questId);
        }

        private void SetOpenImmediate()
        {
            IsOpen = true;
            block.enabled = false;
            if (visual != null) visual.SetActive(false);
            foreach (Transform s in shards) s.gameObject.SetActive(false);
        }

        private void UpdateBreak()
        {
            breakTimer += Time.deltaTime;
            float t = breakTimer / 0.9f;
            for (int i = 0; i < shards.Count; i++)
            {
                Transform s = shards[i];
                shardVel[i] += Physics.gravity * Time.deltaTime;
                s.position += shardVel[i] * Time.deltaTime;
                s.Rotate(shardVel[i] * 40f * Time.deltaTime, Space.World);
                s.localScale *= 1f - Time.deltaTime * 1.2f;
            }
            if (iceMat != null) { Color c = iceColor; c.a *= Mathf.Clamp01(1f - t); iceMat.SetColor("_BaseColor", c); }
            if (t >= 1f)
            {
                breakTimer = -1f;
                foreach (Transform s in shards) s.gameObject.SetActive(false);
            }
        }

        // ───────── 임시 모양: 서리색 반투명 얼음 조각을 막 크기에 맞춰 쌓는다 ─────────

        private void BuildPlaceholder()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            iceMat = new Material(sh) { name = "NMJ_IceGate (임시)", hideFlags = HideFlags.DontSave };
            iceMat.SetFloat("_Surface", 1f);
            iceMat.SetFloat("_Blend", 0f);
            iceMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            iceMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            iceMat.SetFloat("_ZWrite", 0f);
            iceMat.SetFloat("_Smoothness", 0.85f);
            iceMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            iceMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            iceMat.SetColor("_BaseColor", iceColor);

            Vector3 size = block.size, centre = block.center;
            var rng = new System.Random(1887);
            const int cols = 5, rows = 4;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "IceShard";
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = iceMat;
                    go.transform.SetParent(transform, false);
                    float w = size.x / cols, h = size.y / rows;
                    float jx = ((float)rng.NextDouble() - 0.5f) * w * 0.3f, jy = ((float)rng.NextDouble() - 0.5f) * h * 0.3f;
                    go.transform.localPosition = centre + new Vector3(-size.x * 0.5f + w * (c + 0.5f) + jx, -size.y * 0.5f + h * (r + 0.5f) + jy, ((float)rng.NextDouble() - 0.5f) * size.z * 0.4f);
                    go.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 30f - 15f);
                    go.transform.localScale = new Vector3(w * 1.25f, h * 1.3f, size.z * (0.5f + (float)rng.NextDouble() * 0.5f));
                    shards.Add(go.transform);
                    Vector3 outward = transform.forward * (2f + (float)rng.NextDouble() * 3f) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                    shardVel.Add(outward + Vector3.up * (2f + (float)rng.NextDouble() * 3f) + transform.right * (((float)rng.NextDouble() - 0.5f) * 4f));
                }
            }
        }

        private void OnDestroy()
        {
            if (iceMat != null) Destroy(iceMat);
        }

        private void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider>();
            if (b == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.5f, 0.85f, 0.95f, 0.35f);
            Gizmos.DrawCube(b.center, b.size);
        }
    }
}
