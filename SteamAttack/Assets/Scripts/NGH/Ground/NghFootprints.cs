using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 플레이어가 눈 덮인 길(NghFootprintSurface 영역)을 걸으면 발자국을 남긴다.
    ///  - 발이 땅을 디디는 순간을 발 뼈 높이로 잡는다: 발이 내려와 가장 낮은 지점에서 다시 올라가기 시작할 때 한 번 찍음
    ///  - 발자국은 진행 방향으로 돌아간 신발 모양 판(코드로 만든 텍스처). 오래 남았다가 서서히 옅어져 사라짐
    ///  - 구르기·시간 역행 중에는 찍지 않음
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class NghFootprints : MonoBehaviour
    {
        [Header("참조 (비우면 자동)")]
        public PlayerController player;
        [Tooltip("발자국 셰이더 (비우면 NGH/Ink)")]
        public Shader shader;

        [Header("발자국")]
        [Tooltip("발자국 길이(m)")]
        public float length = 0.3f;
        [Tooltip("발자국 폭(m)")]
        public float width = 0.13f;
        [Tooltip("발 뼈가 바닥에서 이 높이(m) 안쪽으로 내려와야 디딘 것으로 봄")]
        public float plantHeight = 0.16f;
        [Tooltip("같은 발로 다시 찍기까지 최소 이동 거리(m)")]
        public float minStride = 0.35f;
        [Tooltip("발자국이 남아 있는 시간(초)")]
        public float lifetime = 25f;
        [Tooltip("사라질 때 옅어지는 시간(초)")]
        public float fadeTime = 4f;
        [Tooltip("동시에 남는 최대 발자국 수")]
        [Range(10, 400)] public int maxPrints = 160;

        class Foot
        {
            public Transform bone; public bool left;
            public float lastY = float.MaxValue; public bool descending; public Vector3 lastPrint = new Vector3(1e6f, 0, 0);
        }
        class Print { public Transform t; public MeshRenderer r; public float born; public float alpha; }

        readonly List<Foot> feet = new List<Foot>();
        readonly List<Print> prints = new List<Print>();
        int next;
        Mesh quad;
        Material mat;
        Texture2D tex;
        MaterialPropertyBlock mpb;
        Transform root;
        TimeRewind rewind;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            rewind = GetComponent<TimeRewind>();
            if (!shader) shader = Shader.Find("NGH/Ink");
        }

        void Start()
        {
            if (!player || !player.animator || !shader) { enabled = false; return; }
            var lf = player.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = player.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lf) feet.Add(new Foot { bone = lf, left = true });
            if (rf) feet.Add(new Foot { bone = rf, left = false });
            Build();
        }

        void OnDestroy()
        {
            if (root) Destroy(root.gameObject);
            if (mat) Destroy(mat);
            if (tex) Destroy(tex);
            if (quad) Destroy(quad);
        }

        void LateUpdate()
        {
            if (feet.Count == 0) return;
            bool blocked = (rewind && rewind.IsRewinding) || player.CurrentAction == PlayerController.ActionState.Roll || player.IsDead;
            foreach (var f in feet)
            {
                Vector3 p = f.bone.position;
                var surf = NghFootprintSurface.Find(p);
                if (!surf || blocked) { f.lastY = p.y; f.descending = false; continue; }
                float h = p.y - surf.topY;
                // 내려오다가 바닥 근처에서 다시 올라가기 시작 = 디딤
                if (p.y < f.lastY - 0.0005f) f.descending = true;
                else if (f.descending && p.y > f.lastY + 0.0005f)
                {
                    f.descending = false;
                    Vector3 flat = new Vector3(p.x, 0f, p.z);
                    if (h < plantHeight && (flat - f.lastPrint).sqrMagnitude > minStride * minStride)
                    {
                        Stamp(surf, p, f.left);
                        f.lastPrint = flat;
                    }
                }
                f.lastY = p.y;
            }

            // 오래된 발자국 옅어지기
            float now = Time.time;
            foreach (var pr in prints)
            {
                if (!pr.t.gameObject.activeSelf) continue;
                float age = now - pr.born;
                if (age < lifetime) continue;
                float k = 1f - Mathf.Clamp01((age - lifetime) / Mathf.Max(0.01f, fadeTime));
                if (k <= 0f) { pr.t.gameObject.SetActive(false); continue; }
                SetAlpha(pr, k);
            }
        }

        void Stamp(NghFootprintSurface surf, Vector3 footPos, bool left)
        {
            // 발 앞쪽 방향 = 몸 방향 (발 뼈 방향은 모델마다 달라 몸 기준이 안정적)
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            // 발 뼈는 발목이라 발자국 가운데는 조금 앞쪽, 좌우로 살짝 바깥
            Vector3 pos = new Vector3(footPos.x, surf.topY + 0.004f, footPos.z) + fwd * (length * 0.3f) + side * (left ? -0.01f : 0.01f);

            Print pr;
            if (prints.Count < maxPrints) { pr = NewPrint(); prints.Add(pr); }
            else { pr = prints[next]; next = (next + 1) % prints.Count; }
            pr.t.gameObject.SetActive(true);
            pr.t.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.AngleAxis(Random.Range(-6f, 6f), Vector3.up));
            pr.t.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);   // 왼발은 좌우 뒤집음
            pr.born = Time.time;
            pr.alpha = surf.printColor.a;
            mpb.SetColor(ColorId, surf.printColor);
            pr.r.SetPropertyBlock(mpb);
        }

        void SetAlpha(Print pr, float k)
        {
            pr.r.GetPropertyBlock(mpb);
            Color c = mpb.GetColor(ColorId);
            c.a = pr.alpha * k;
            mpb.SetColor(ColorId, c);
            pr.r.SetPropertyBlock(mpb);
        }

        Print NewPrint()
        {
            var go = new GameObject("Footprint");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return new Print { t = go.transform, r = r };
        }

        void Build()
        {
            root = new GameObject("NGH_Footprints").transform;
            mpb = new MaterialPropertyBlock();
            tex = SoleTexture(64, 144);
            mat = new Material(shader) { name = "NGH_Footprint", renderQueue = 2450 };
            mat.mainTexture = tex;
            if (mat.HasProperty("_Sharp")) mat.SetFloat("_Sharp", 2.5f);

            // 바닥에 눕힌 판 (앞 = +Z), 정점색 흰색 (NGH/Ink 는 정점 알파로 깎으므로 1)
            quad = new Mesh { name = "NGH_FootprintQuad" };
            float hw = width * 0.5f, hl = length * 0.5f;
            quad.vertices = new[] { new Vector3(-hw, 0, -hl), new Vector3(-hw, 0, hl), new Vector3(hw, 0, hl), new Vector3(hw, 0, -hl) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
        }

        // 신발 바닥 모양 (알파 = 눌린 정도). 앞꿈치 넓은 타원 + 뒤꿈치 작은 타원, 안쪽 아치는 오목. 가장자리는 부드럽게
        static Texture2D SoleTexture(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "NGH_FootprintSole", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;        // -1..1 (폭)
                float v = (y + 0.5f) / h;                  // 0 뒤꿈치 .. 1 발끝
                // 앞꿈치: 중심 v=0.68, 반지름 폭 0.95, 길이 0.32 / 뒤꿈치: v=0.18, 폭 0.72, 길이 0.17
                float fore = Ellipse(u - 0.05f, v - 0.68f, 0.92f, 0.31f);
                float heel = Ellipse(u + 0.04f, v - 0.17f, 0.70f, 0.16f);
                float arch = Ellipse(u + 0.18f, v - 0.42f, 0.55f, 0.16f);   // 아치 (안쪽이 살짝 덜 눌림)
                float a = Mathf.Max(fore, heel, arch * 0.55f);
                // 눈이 눌린 결: 가로줄 무늬를 살짝
                float tread = 0.85f + 0.15f * Mathf.Sin(v * 70f);
                a = Mathf.Clamp01(a * tread);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static float Ellipse(float dx, float dy, float rx, float ry)
        {
            float d = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            return Mathf.Clamp01((1f - d) * 3.5f);   // 가장자리 부드럽게
        }
    }
}
