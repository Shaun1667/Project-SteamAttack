using System.Collections.Generic;
using NGH;
using UnityEngine;
using UnityEngine.SceneManagement;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 검 이펙트 시안 비교 (이펙트 기획 NMJ · 제작 NGH, D-019). 개발자 창 "검 이펙트"로 바꾼다.
    /// ※ 기존 NGH 이펙트는 그대로 두고, 강공격에만 칼금·불티(C3는 붉은 김)를 살짝 얹는다.
    ///   설명·연출 기준: TeamWorkflow/Requests/진행/NGH_요청_20261023_강공격붉은칼금.md
    ///
    /// ※ 강공격에만 적용. 일반 공격(1~5타)은 어느 설정이든 기존 NGH 이펙트.
    ///   기존  NGH 베기 이펙트 그대로 (기본값 — 이 컴포넌트는 아무것도 안 함)
    ///   C2    붉은 칼금: 칼 바로 뒤가 가장 두툼하고 뜨거운 주홍, 오래된 꼬리는 바늘처럼 가늘고 검붉게 식음.
    ///         맞히면 심이 한 번 번쩍이고 베는 방향으로 불티. 사라질 때 칼금이 끊어지며 불씨로 바뀜
    ///   C3    C2 + 붉은 김: 김까지 붉게 (세계관 법칙 5 "증기통이 가득 차면 붉은 증기")
    ///   자동  증기가 가득 차 있으면 C3, 아니면 C2
    ///
    /// - 칼 궤적은 NGH.AttackFx가 애니메이션에서 미리 계산해 둔 경로(entry.pathOuter / pathInner, 플레이어 기준)를 그대로 쓴다
    ///   → 칼과 정확히 겹친다. 경로가 없는 항목(찌르기 등)은 건드리지 않는다.
    /// - 칼이 경로의 각 점을 지난 시각을 정점(UV1)에 적어 두고, 셰이더가 '지난 뒤 흐른 시간'으로 굵기·색을 정한다
    ///   → 빠르게 벤 구간은 길게, 느린 구간은 짧게 남는다.
    /// - NGH 파일은 고치지 않는다 (PlayerController.OnHitLanded 를 듣기만 함).
    /// - 그림: Resources/NMJ/SwordFX (셰이더 2, 김 텍스처 1)
    /// </summary>
    [DefaultExecutionOrder(1150)]   // AttackFx(1100) 다음
    [DisallowMultipleComponent]
    public class NmjSwordSlash : MonoBehaviour
    {
        public enum Mode { Original, C2, C3, Auto, Flair }

        public static Mode Current { get; private set; } = Mode.Original;

        public static string Label(Mode m) => m switch
        {
            Mode.C2 => "C2 붉은 칼금",
            Mode.C3 => "C3 붉은 김",
            Mode.Auto => "자동 (증기 가득이면 C3, 아니면 C2)",
            Mode.Flair => "화려판 (맞는 순간 + 증기 단계 + 일섬)",
            _ => "기존 (NGH)",
        };

        public static void SetMode(Mode m) => Current = m;
        public static Mode Cycle() { Current = (Mode)(((int)Current + 1) % 5); return Current; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = Mode.Original;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Attach();

        private static void Attach()
        {
            foreach (AttackFx fx in FindObjectsByType<AttackFx>(FindObjectsInactive.Include))
                if (fx.GetComponent<NmjSwordSlash>() == null) fx.gameObject.AddComponent<NmjSwordSlash>();
        }

        // ───────── 모양 값 ─────────

        private const string Folder = "NMJ/SwordFX/";
        private static readonly Color Red = new Color(0.82f, 0.18f, 0.10f);
        private static readonly Color SteamC2 = new Color(1.0f, 0.96f, 0.95f, 0.5f);
        private static readonly Color SteamC3 = new Color(0.80f, 0.21f, 0.15f, 0.3f);   // 옅게 겹쳐 안개처럼 (진하면 동글동글한 점으로 보임)
        private static readonly Color RibbonTintC3 = new Color(0.80f, 0.20f, 0.14f);   // C3 붉은 김 색

        private class Look
        {
            public int whiffEmbers;      // 휘두르는 동안 (헛스윙에도) 빠른 구간에서 튀는 불티
            public int hitEmbers;        // 맞힌 순간 베는 방향으로 터지는 불티
            public int hitDrops;         // 맞힌 순간 주묵 방울
            public int cinders;          // 사라질 때 칼금이 끊어지며 생기는 불씨
            public int puffs;            // 붉은 김
            public Vector2 puffSize;
            public float puffInner;      // 김이 나오는 폭 (칼끝=1 기준 안쪽 한계)
            public float life;           // 칼이 지난 뒤 칼금이 다 식는 시간(초)
            public float fade;           // 다 그은 뒤 사라지는 시간(초)
            public float edgeWidth = 0.085f;   // 칼금 굵기 (일반 공격은 가늘게)
            public bool whiteSteam;      // 김을 흰색으로 (증기 50~99 일반 공격)
            public bool burst;           // 증기 분출 베기: 다 그은 순간 발밑에 붉은 김 고리
        }
        private static readonly Look LookC2 = new Look { whiffEmbers = 3, hitEmbers = 8, hitDrops = 3, cinders = 14, puffs = 0, puffSize = new Vector2(0.10f, 0.2f), puffInner = 0.9f, life = 0.32f, fade = 0.45f };
        private static readonly Look LookC3 = new Look { whiffEmbers = 4, hitEmbers = 14, hitDrops = 4, cinders = 20, puffs = 10, puffSize = new Vector2(0.30f, 0.48f), puffInner = 0.88f, life = 0.40f, fade = 0.5f };
        // 화려판(Flair): 증기 단계
        //   일반 공격  증기 50~99 = 가는 주홍 칼금 + 흰 김 몇 줄 / 증기 100 = 붉은 김이 칼을 휘감음
        //   강공격     증기 100으로 시작 = 증기 분출 베기 (칼금이 길게 남고, 다 그은 순간 발밑에 붉은 김 고리)
        private static readonly Look LookLight1 = new Look { whiffEmbers = 1, hitEmbers = 3, hitDrops = 0, cinders = 6, puffs = 4, puffSize = new Vector2(0.14f, 0.24f), puffInner = 0.9f, life = 0.22f, fade = 0.3f, edgeWidth = 0.05f, whiteSteam = true };
        private static readonly Look LookLight2 = new Look { whiffEmbers = 2, hitEmbers = 5, hitDrops = 1, cinders = 10, puffs = 8, puffSize = new Vector2(0.22f, 0.36f), puffInner = 0.85f, life = 0.28f, fade = 0.36f, edgeWidth = 0.06f };
        private static readonly Look LookBurst = new Look { whiffEmbers = 6, hitEmbers = 16, hitDrops = 5, cinders = 26, puffs = 16, puffSize = new Vector2(0.32f, 0.52f), puffInner = 0.85f, life = 0.55f, fade = 0.6f, edgeWidth = 0.1f, burst = true };

        private float lastPressure, actionPressure;   // 동작 시작 직전의 증기 (강공격은 시작하며 30을 쓰므로 그 전 값으로 단계를 정함)
        private bool ActionFull => actionPressure >= MaxPressure() - 0.5f;

        // ───────── 상태 ─────────

        private PlayerController player;
        private AttackFx fx;
        private readonly List<AttackFx.Entry> pending = new List<AttackFx.Entry>();
        private int serial = -1;
        private readonly List<Slash> active = new List<Slash>();
        private readonly Stack<Slash> pool = new Stack<Slash>();

        private Material trailMat;
        private ParticleSystem psRed, psEmber, psCinder, psPuff;
        private Transform fxRoot;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            fx = GetComponent<AttackFx>();
        }

        private void OnEnable() { if (player != null) player.OnHitLanded += OnHitLanded; }
        private void OnDisable() { if (player != null) player.OnHitLanded -= OnHitLanded; }

        private void OnDestroy()
        {
            if (fxRoot != null) Destroy(fxRoot.gameObject);
        }

        private void Update()
        {
            if (player == null || fx == null) return;
            if (Current == Mode.Original) { serial = player.ActionSerial; pending.Clear(); return; }

            if (player.ActionSerial != serial)
            {
                serial = player.ActionSerial;
                actionPressure = lastPressure;
                pending.Clear();
                foreach (AttackFx.Entry e in fx.entries)
                    if (Eligible(e) && e.action == player.CurrentAction) pending.Add(e);
            }
            float t = player.ActionClipProgress;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                AttackFx.Entry e = pending[i];
                if (player.CurrentAction != e.action) { pending.RemoveAt(i); continue; }
                if (t >= e.spawnAt) { Spawn(e); pending.RemoveAt(i); }
            }
        }

        // 애니메이션이 끝난 뒤 머리 위치를 맞추고 입자를 뿌린다
        private void LateUpdate()
        {
            lastPressure = CurrentPressure();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Slash s = active[i];
                if (!s.Tick(this)) { s.go.SetActive(false); active.RemoveAt(i); pool.Push(s); }
            }
        }

        // 맞힌 순간: 지금 긋고 있는 강공격 칼금에 섬광·불티
        private void OnHitLanded(Damageable target)
        {
            if (Current == Mode.Original) return;
            bool heavy = player.CurrentAction == PlayerController.ActionState.HeavyAttack;
            Slash cur = null;
            foreach (Slash s in active)
                if (s.Serial == player.ActionSerial) { cur = s; break; }
            if (heavy && cur != null) cur.Hit(this);

            // 화려판: 맞는 순간 연출 (일반 공격도)
            if (Current != Mode.Flair) return;
            Vector3 pos = target != null ? target.AimPoint : player.transform.position + Vector3.up * 1.2f + player.transform.forward;
            Vector3 dir = cur != null ? cur.HeadTangent() : player.transform.right;
            NmjSwordFlair.Get(this).Impact(pos, dir, heavy, heavy && ActionFull);
        }

        private static bool IsLight(PlayerController.ActionState a) =>
            a == PlayerController.ActionState.LightAttack || a == PlayerController.ActionState.LightCombo2 || a == PlayerController.ActionState.LightCombo3
            || a == PlayerController.ActionState.LightCombo4 || a == PlayerController.ActionState.LightCombo5;

        private static bool HasPath(AttackFx.Entry e) =>
            e != null && e.kind == AttackFx.FxKind.Slash && e.pathOuter != null && e.pathOuter.Length >= 2
            && e.pathInner != null && e.pathInner.Length == e.pathOuter.Length;

        // 시안은 강공격에만. 화려판은 증기 50 이상이면 일반 공격에도 얹는다
        private bool Eligible(AttackFx.Entry e) =>
            HasPath(e) && (e.action == PlayerController.ActionState.HeavyAttack
                           || (Current == Mode.Flair && IsLight(e.action) && actionPressure >= MaxPressure() * 0.5f));

        private static float CurrentPressure()
        {
            float p = 0f;
            foreach (SteamTank tank in NmjStartTutorial.FindTanks()) if (tank != null) p = Mathf.Max(p, tank.CurrentPressure);
            return p;
        }

        private static float MaxPressure()
        {
            float m = 0f;
            foreach (SteamTank tank in NmjStartTutorial.FindTanks()) if (tank != null) m = Mathf.Max(m, tank.MaxPressure);
            return m > 0f ? m : 100f;
        }

        private void Spawn(AttackFx.Entry e)
        {
            EnsureResources();
            Slash s = pool.Count > 0 ? pool.Pop() : new Slash(trailMat);
            bool heavy = e.action == PlayerController.ActionState.HeavyAttack;
            bool full = ActionFull;
            Look look;
            bool red;
            if (heavy)
            {
                red = Current == Mode.C3 || ((Current == Mode.Auto || Current == Mode.Flair) && full);
                look = Current == Mode.Flair && full ? LookBurst : red ? LookC3 : LookC2;
            }
            else
            {
                red = full;
                look = full ? LookLight2 : LookLight1;
            }
            s.Begin(e, player, red, look);
            active.Add(s);
        }

        /// <summary>증기 분출 베기: 발밑에서 붉은 김이 고리로 퍼진다</summary>
        private void SteamRing(Vector3 center)
        {
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Emit(psPuff, center + d * 0.5f + Vector3.up * 0.15f, d * Random.Range(2.2f, 3.2f) + Vector3.up * Random.Range(0.1f, 0.4f),
                     Random.Range(0.35f, 0.55f), Random.Range(0.6f, 0.9f), SteamC3);
            }
        }

        // ───────── 그림 재료 ─────────

        private void EnsureResources()
        {
            if (fxRoot != null) return;
            fxRoot = new GameObject("NMJ_SwordFX").transform;
            fxRoot.SetParent(transform, false);

            Shader trail = Resources.Load<Shader>(Folder + "NmjSwordSlash");
            Shader part = Resources.Load<Shader>(Folder + "NmjSwordSlashParticle");
            Texture2D puff = Resources.Load<Texture2D>(Folder + "T_SwordSteam_Flip");   // 김 연속 그림 4×4 (뭉쳤다가 결 따라 흩어짐)
            Texture2D bristle = Resources.Load<Texture2D>(Folder + "T_SwordInk_Bristle");   // 붓 결
            if (bristle != null) bristle.wrapMode = TextureWrapMode.Repeat;
            if (puff != null) puff.wrapMode = TextureWrapMode.Clamp;
            Texture2D dot = MakeDot();

            trailMat = new Material(trail) { name = "NMJ_SwordSlash" };
            trailMat.renderQueue = 3010;   // 기존 흰 베기 이펙트(3000) 위에 그려지게
            trailMat.SetTexture("_BrushTex", bristle);

            // 불티: 흰 → 놋쇠 → 주홍 → 검붉음으로 식으며 작아짐. 막 튄 순간만 빛 번짐
            Gradient hot = Ramp(new[] { (new Color(1f, 0.95f, 0.85f), 0f), (new Color(1f, 0.65f, 0.28f), 0.2f), (new Color(0.95f, 0.22f, 0.10f), 0.5f), (new Color(0.30f, 0.04f, 0.03f), 1f) },
                                new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });
            // 불씨: 주홍에서 시작해 검붉게 식음
            Gradient cinder = Ramp(new[] { (new Color(1f, 0.6f, 0.25f), 0f), (new Color(0.95f, 0.22f, 0.10f), 0.4f), (new Color(0.25f, 0.04f, 0.03f), 1f) },
                                   new[] { (1f, 0f), (0.9f, 0.5f), (0f, 1f) });

            psRed = MakeSystem("RedInk", part, dot, gravity: 1.3f, stretch: true, grow: 0.6f, drag: 0.6f);
            psEmber = MakeSystem("Ember", part, dot, gravity: 0.6f, stretch: true, grow: 0f, drag: 3f, intensity: 1.6f, color: hot, velocityScale: 0.03f, lengthScale: 1f);
            psCinder = MakeSystem("Cinder", part, dot, gravity: -0.05f, stretch: false, grow: 0.2f, drag: 2f, intensity: 1.3f, color: cinder);
            psPuff = MakeSystem("Steam", part, puff, gravity: -0.04f, stretch: false, grow: 2.2f, drag: 2.2f, fadeIn: true);
            var sheet = psPuff.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = 4; sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet; sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        }

        private static Gradient Ramp((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            var g = new Gradient();
            g.SetKeys(ck, ak);
            return g;
        }

        private ParticleSystem MakeSystem(string name, Shader shader, Texture tex, float gravity, bool stretch, float grow, float drag, bool fadeIn = false, int blend = 0, float intensity = 1f,
                                          Gradient color = null, float velocityScale = 0.05f, float lengthScale = 1.6f)
        {
            var go = new GameObject("NMJ_SwordFX_" + name);
            go.transform.SetParent(fxRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = color ?? Ramp(new[] { (Color.white, 0f), (Color.white, 1f) },
                                      fadeIn ? new[] { (0f, 0f), (1f, 0.12f), (0.6f, 0.6f), (0f, 1f) } : new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });

            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, grow));

            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = drag; lv.limit = 100f;

            if (!stretch)
            {
                var rot = ps.rotationOverLifetime; rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(shader) { name = "NMJ_SwordFX_" + name };
            mat.renderQueue = 3010;
            mat.SetTexture("_MainTex", tex);
            // 포토샵 레이어 혼합: 0 보통 · 1 곱하기 · 2 더하기(HDR)
            mat.SetFloat("_Mode", blend);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_SrcBlend", blend == 1 ? (float)UnityEngine.Rendering.BlendMode.DstColor : blend == 2 ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", blend == 1 ? (float)UnityEngine.Rendering.BlendMode.Zero : blend == 2 ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            r.sharedMaterial = mat;
            r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch) { r.velocityScale = velocityScale; r.lengthScale = lengthScale; }
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static Texture2D MakeDot()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "NMJ_SwordFX_Dot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float k = Mathf.Clamp01((r - 0.30f) / 0.20f);   // 0.30 안쪽은 진하게, 0.5에서 0
                    float a = 1f - k * k * (3f - 2f * k);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        // 셰이더 TaperX와 같은 비대칭 굵기 (불씨를 칼금이 남아 있는 곳에만 내려고)
        private static float TaperX(float x)
        {
            x = Mathf.Clamp01(x);
            return Mathf.Pow(x, 1.6f) * Mathf.Pow(1f - x, 0.25f) / 0.4806f;
        }

        // ───────── 베기 하나 ─────────

        private class Slash
        {
            public readonly GameObject go;
            private readonly Mesh mesh;
            private readonly MeshRenderer renderer;
            private readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Vector2> times = new List<Vector2>();
            private readonly List<int> tris = new List<int>();

            private AttackFx.Entry entry;
            private PlayerController player;
            private Vector3[] outer, inner;
            private float[] passT;           // 칼이 경로의 각 점을 지난 시각 (clock 기준)
            private int reached;             // 칼이 지난 마지막 점
            private int serial, prefix;
            private bool red, bursted;
            private Look look;
            private float head, hold, fadeT, clock, flash;
            private int burstLeft;
            private float carryEmber, carryPuff, carryCinder;

            public int Serial => serial;

            private static readonly int HeadId = Shader.PropertyToID("_Head");
            private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
            private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
            private static readonly int TintId = Shader.PropertyToID("_Tint");
            private static readonly int SteamId = Shader.PropertyToID("_Steam");
            private static readonly int NowId = Shader.PropertyToID("_Now");
            private static readonly int LifeId = Shader.PropertyToID("_Life");
            private static readonly int CoreBoostId = Shader.PropertyToID("_CoreBoost");
            private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");

            public Slash(Material mat)
            {
                go = new GameObject("NMJ_SwordSlash");
                mesh = new Mesh { name = "NMJ_SwordSlash" };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            public void Begin(AttackFx.Entry e, PlayerController p, bool redSteam, Look lk)
            {
                entry = e; player = p; red = redSteam;
                look = lk; bursted = false;
                outer = e.pathOuter; inner = e.pathInner; prefix = Mathf.Clamp(e.pathPrefix, 0, outer.Length - 2);
                serial = p.ActionSerial;
                head = 0f; hold = 0f; fadeT = -1f; clock = 0f; flash = 0f; burstLeft = 0;
                carryEmber = carryPuff = carryCinder = 0f;
                if (passT == null || passT.Length != outer.Length) passT = new float[outer.Length];
                passT[0] = 0f; reached = 0;

                // 플레이어를 따라 움직인다 (공격 중 앞으로 나가도 칼과 어긋나지 않게) — 경로가 플레이어 기준이라 그대로 꼭짓점으로 쓴다
                go.transform.SetParent(p.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                BuildMesh();
                go.SetActive(true);
                Apply(0f, 0f, 1f);
            }

            private void BuildMesh()
            {
                verts.Clear(); uvs.Clear(); tris.Clear();
                int n = outer.Length;
                for (int i = 0; i < n; i++)
                {
                    float u = (float)i / (n - 1);
                    verts.Add(inner[i]); uvs.Add(new Vector2(u, 0f));
                    verts.Add(outer[i]); uvs.Add(new Vector2(u, 1f));
                }
                for (int i = 0; i < n - 1; i++)
                {
                    int a = i * 2;
                    tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                    tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
                }
                mesh.Clear();
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                WriteTimes();
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
            }

            // 칼이 지난 점은 지난 시각, 아직 안 지난 점은 '지금'
            private void WriteTimes()
            {
                times.Clear();
                for (int i = 0; i < outer.Length; i++)
                {
                    float t = i <= reached ? passT[i] : clock;
                    times.Add(new Vector2(t, 0f)); times.Add(new Vector2(t, 0f));
                }
                mesh.SetUVs(1, times);
            }

            private void Apply(float headU, float dissolve, float opacity)
            {
                WriteTimes();
                renderer.GetPropertyBlock(mpb);
                mpb.SetFloat(HeadId, headU * 1.06f);
                mpb.SetFloat(DissolveId, dissolve);
                mpb.SetFloat(OpacityId, opacity);
                mpb.SetColor(TintId, RibbonTintC3);
                mpb.SetFloat(SteamId, red ? 1f : 0f);
                mpb.SetFloat(EdgeWidthId, look.edgeWidth);
                mpb.SetFloat(NowId, clock);
                mpb.SetFloat(LifeId, look.life);
                mpb.SetFloat(CoreBoostId, flash);
                renderer.SetPropertyBlock(mpb);
            }

            /// <summary>맞힌 순간: 심이 한 번 번쩍, 불티는 2~3프레임에 걸쳐 '타다닥', 주묵 방울</summary>
            /// <summary>지금 칼이 있는 곳의 베는 방향 (세계)</summary>
            public Vector3 HeadTangent() { Frame(player.transform, head, 1f, out _, out Vector3 tan, out _); return tan; }

            public void Hit(NmjSwordSlash owner)
            {
                flash = 1f;
                burstLeft = look.hitEmbers;
                Transform t = player.transform;
                for (int i = 0; i < look.hitDrops; i++) Drop(owner.psRed, t, head, Red, 0.01f, 0.022f);
            }

            /// <summary>false = 다 사라짐 (풀로 돌려보냄)</summary>
            public bool Tick(NmjSwordSlash owner)
            {
                if (player == null) return false;
                float dt = Time.deltaTime;
                float prevClock = clock;
                clock += fadeT < 0f ? dt : dt * 0.35f;   // 다 그은 뒤에는 천천히 식힘 — 칼금이 흰 초승달보다 오래 남고, 사라짐은 침식(타는 테·불씨)이 맡는다
                flash = Mathf.Max(0f, flash - dt / 0.1f);
                if (burstLeft > 0)
                {
                    int k = Mathf.Min(burstLeft, Mathf.CeilToInt(look.hitEmbers / 3f));
                    burstLeft -= k;
                    for (int i = 0; i < k; i++) Spark(owner, head + Random.Range(-0.04f, 0.01f), 5f, 10f);
                }

                if (fadeT < 0f)
                {
                    // 칼 위치(클립 진행도)를 따라 머리를 민다. 동작이 끊기거나 바뀌면 끝까지
                    bool same = player.ActionSerial == serial && player.CurrentAction == entry.action;
                    float swing = same ? Mathf.InverseLerp(entry.swingRange.x, entry.swingRange.y, player.ActionClipProgress) : 1f;
                    int n = outer.Length;
                    float u = (prefix + swing * (n - 1 - prefix)) / (n - 1);
                    if (u > head) { MarkPassed(head, u, prevClock, clock); EmitAlong(owner, head, u); head = u; }
                    if (head >= 0.999f)
                    {
                        MarkPassed(head, 1.001f, clock, clock);   // 끝점까지 지난 것으로 (그래야 머리도 식는다)
                        if (look.burst && !bursted) { bursted = true; owner.SteamRing(player.transform.position); }
                        hold += dt; if (hold >= 0.05f) fadeT = 0f;
                    }
                    Apply(head, 0f, 1f);
                    return true;
                }
                float k0 = Mathf.Clamp01(fadeT / look.fade);
                fadeT += dt;
                float k1 = Mathf.Clamp01(fadeT / look.fade);
                float dissolve = Mathf.Pow(k1, 1.3f);   // 처음엔 천천히, 끝에 빠르게
                EmitCinders(owner, dissolve, k1 - k0);
                Apply(1f, dissolve, 1f - Mathf.SmoothStep(0.8f, 1f, k1));
                return k1 < 1f;
            }

            // 머리가 h0 → h1 로 가는 동안 지난 점들에 지난 시각을 적는다
            private void MarkPassed(float h0, float h1, float c0, float c1)
            {
                int n = outer.Length;
                for (int i = reached + 1; i < n; i++)
                {
                    float u = (float)i / (n - 1);
                    if (u > h1) break;
                    passT[i] = Mathf.Lerp(c0, c1, Mathf.InverseLerp(h0, h1, u));
                    reached = i;
                }
            }

            // u0 → u1 사이로 칼이 지나가는 동안: 빠른 구간(아크 40~90%)에서 불티 몇 개, C3는 붉은 김
            private void EmitAlong(NmjSwordSlash owner, float u0, float u1)
            {
                float du = u1 - u0;
                float fast = Mathf.Max(0f, Mathf.Min(u1, 0.9f) - Mathf.Max(u0, 0.4f));
                carryEmber += look.whiffEmbers * fast / 0.5f;
                carryPuff += look.puffs * du;
                Transform t = player.transform;
                Color steam = look.whiteSteam ? SteamC2 : red ? SteamC3 : SteamC2;

                while (carryEmber >= 1f) { carryEmber -= 1f; Spark(owner, Random.Range(u0, u1), 4f, 8f); }
                while (carryPuff >= 1f)
                {
                    carryPuff -= 1f;
                    float u = Random.Range(u0, u1);
                    Frame(t, u, Random.Range(look.puffInner, 1.05f), out Vector3 pos, out Vector3 tan, out Vector3 rad);
                    pos += rad * Random.Range(0f, 0.12f) + tan * Random.Range(-0.12f, 0.12f);
                    Vector3 vel = Vector3.up * Random.Range(0.3f, 0.6f) + rad * Random.Range(0.1f, 0.4f) + tan * Random.Range(0f, 0.5f);
                    float size = Random.Range(look.puffSize.x, look.puffSize.y) * (0.6f + 0.6f * u);
                    Emit(owner.psPuff, pos, vel, size, Random.Range(0.6f, 1.0f), steam);
                }
            }

            // 불티: 베는 방향(접선) ±25° 원뿔로. 사방으로 터뜨리지 않는다
            private void Spark(NmjSwordSlash owner, float u, float speedMin, float speedMax)
            {
                Frame(player.transform, u, Random.Range(0.85f, 1.02f), out Vector3 pos, out Vector3 tan, out Vector3 rad);
                Vector3 dir = (tan + rad * Random.Range(-0.45f, 0.45f) + Vector3.up * Random.Range(-0.25f, 0.45f)).normalized;
                Emit(owner.psEmber, pos, dir * Random.Range(speedMin, speedMax), Random.Range(0.010f, 0.018f), Random.Range(0.15f, 0.35f), Color.white);
            }

            // 사라질 때: 칼금이 먹혀 들어가는 경계(타는 테) 근처에서 불씨가 생긴다 → 면에서 점으로 바통 넘기기
            // 셰이더의 e = n·0.6 + x·0.8 + v·0.3 이 문턱 T(=dissolve·1.7)와 만나는 곳을 칼끝(v≈0.97), 노이즈 평균(0.5)으로 어림
            private void EmitCinders(NmjSwordSlash owner, float dissolve, float dk)
            {
                float xFront = (dissolve * 1.7f - 0.54f) / 0.8f;
                if (xFront <= 0f || xFront >= 1f) return;
                carryCinder += look.cinders * dk / 0.7f;
                if (carryCinder < 1f) return;

                // 젊음 x 가 xFront 인 곳 = 칼이 (지금 - life·(1 - xFront)) 에 지난 곳
                float target = clock - look.life * (1f - xFront);
                int n = outer.Length;
                if (target < passT[0] || target > passT[n - 1]) { carryCinder = 0f; return; }
                int i = 1;
                while (i < n - 1 && passT[i] < target) i++;
                float uFront = (i - 1 + Mathf.InverseLerp(passT[i - 1], passT[i], target)) / (n - 1);
                if (TaperX(xFront) * Mathf.Pow(Mathf.Sin(Mathf.PI * uFront), 0.4f) < 0.15f) { carryCinder = 0f; return; }

                Transform t = player.transform;
                while (carryCinder >= 1f)
                {
                    carryCinder -= 1f;
                    Frame(t, uFront + Random.Range(-0.03f, 0.03f), Random.Range(0.93f, 1.0f), out Vector3 pos, out Vector3 tan, out Vector3 rad);
                    Vector3 vel = Vector3.up * Random.Range(0.1f, 0.3f) + tan * Random.Range(-0.2f, 0.4f) + rad * Random.Range(0f, 0.15f);
                    Emit(owner.psCinder, pos, vel, Random.Range(0.008f, 0.016f), Random.Range(0.3f, 0.5f), Color.white);
                }
            }

            private void Drop(ParticleSystem ps, Transform t, float u, Color c, float min, float max)
            {
                Frame(t, u, 1f, out Vector3 pos, out Vector3 tan, out Vector3 rad);
                pos += rad * Random.Range(0f, 0.15f);
                Vector3 vel = tan * Random.Range(1.5f, 4.5f) + rad * Random.Range(0.5f, 2.5f) + Vector3.up * Random.Range(-0.3f, 1.5f);
                Emit(ps, pos, vel, Random.Range(min, max), Random.Range(0.4f, 0.6f), c);
            }

            // 경로 위 u(0~1), 폭 s(0 안쪽 ~ 1 칼끝)의 세계 위치와 칼이 나아가는 방향·바깥 방향
            private void Frame(Transform t, float u, float s, out Vector3 pos, out Vector3 tan, out Vector3 rad)
            {
                int n = outer.Length;
                float f = Mathf.Clamp01(u) * (n - 1);
                int i = Mathf.Min((int)f, n - 2);
                float w = f - i;
                Vector3 o = Vector3.Lerp(outer[i], outer[i + 1], w);
                Vector3 inn = Vector3.Lerp(inner[i], inner[i + 1], w);
                pos = t.TransformPoint(Vector3.LerpUnclamped(inn, o, s));
                tan = t.TransformDirection(outer[i + 1] - outer[i]).normalized;
                rad = t.TransformDirection(o - inn).normalized;
            }
        }
    }
}
