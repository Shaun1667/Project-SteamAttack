using System.Collections;
using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// 검 이펙트 '화려판' 시험 (NmjSwordSlash.Mode.Flair 일 때만). 기존 NGH 파일은 고치지 않는다.
    ///  안3 맞는 순간: 히트스톱(강 0.09초 · 약 0.04초) + 칼 방향으로 납작한 섬광 + 베인 방향 칼금 선 + 카메라가 베는 방향으로 한 번 밀림
    ///                받아치기 성공: 칼 부딪는 섬광 + 사방 불티 고리
    ///  안2 일섬: 증기 가득(100) 강공격이 맞으면 0.07초 동안 화면이 종이·먹 두 색으로 반전되고, 맞은 자리에 주홍 낙관(討)이 찍힘
    ///  (안1 증기 단계는 NmjSwordSlash 에서)
    /// </summary>
    [DisallowMultipleComponent]
    public class NmjSwordFlair : MonoBehaviour
    {
        private const string Folder = "NMJ/SwordFX/";
        private static NmjSwordFlair instance;
        /// <summary>촬영용: 실시간 연출(히트스톱·섬광·일섬·낙관)을 이 배율만큼 길게 (평소 1)</summary>
        public static float DebugSlow = 1f;

        private Material flashMat, lineMat, cutMat, splatMat;
        private Texture2D streakTex;
        private ParticleSystem sparks;
        private Transform root;
        private Coroutine stopRoutine;
        private float stopUntil = -1f, stopRestore = 1f;
        private TMP_FontAsset glyphFont;

        public static NmjSwordFlair Get(Component near)
        {
            if (instance == null)
            {
                instance = near.GetComponent<NmjSwordFlair>();
                if (instance == null) instance = near.gameObject.AddComponent<NmjSwordFlair>();
            }
            return instance;
        }

        private void OnEnable() => PlayerParry.ParryAttempted += OnParry;
        private void OnDisable() => PlayerParry.ParryAttempted -= OnParry;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (root != null) Destroy(root.gameObject);
            if (stopUntil > 0f) Time.timeScale = stopRestore;
        }

        private void Ensure()
        {
            if (root != null) return;
            root = new GameObject("NMJ_SwordFlair").transform;
            Shader part = Resources.Load<Shader>(Folder + "NmjSwordSlashParticle");
            streakTex = MakeStreak();
            flashMat = NewAlphaMat(part, streakTex, 3.2f);
            var splatTex = Resources.Load<Texture2D>(Folder + "T_SwordInk_Splat");
            splatMat = NewAlphaMat(part, splatTex, 1f);
            lineMat = NewAlphaMat(part, streakTex, 2.2f);
            cutMat = new Material(Resources.Load<Shader>(Folder + "NmjInkCut")) { name = "NMJ_InkCut" };

            var go = new GameObject("NMJ_SwordFlair_Sparks");
            go.transform.SetParent(root, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.gravityModifier = 0.6f; main.maxParticles = 300; main.useUnscaledTime = true;
            var em = sparks.emission; em.enabled = false;
            var sh = sparks.shape; sh.enabled = false;
            var col = sparks.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.25f), 0.3f), new GradientColorKey(new Color(0.9f, 0.2f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var lv = sparks.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 3f; lv.limit = 100f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = NewAlphaMat(part, streakTex, 1.8f);
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparks.Play();
        }

        private static Material NewAlphaMat(Shader shader, Texture tex, float intensity)
        {
            var m = new Material(shader) { name = "NMJ_SwordFlair", renderQueue = 3020 };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Mode", 0f);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return m;
        }

        // 가로로 긴 부드러운 줄 (가운데가 진함)
        private static Texture2D MakeStreak()
        {
            const int W = 128, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "NMJ_Streak" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
                    float a = Mathf.Exp(-v * v * 9f) * Mathf.Pow(Mathf.Clamp01(1f - u * u), 1.5f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        // ───────── 안3 맞는 순간 ─────────

        /// <summary>칼이 맞은 순간. dir = 베는 방향(세계), heavy = 강공격, full = 증기 가득(100)으로 시작한 강공격</summary>
        public void Impact(Vector3 pos, Vector3 dir, bool heavy, bool full)
        {
            Ensure();
            Camera cam = Camera.main;
            if (dir.sqrMagnitude < 1e-4f) dir = cam != null ? cam.transform.right : Vector3.right;
            dir.Normalize();

            HitStop((heavy ? 0.09f : 0.04f) * DebugSlow);
            StartCoroutine(Flash(pos, dir, heavy ? 1.8f : 1.1f, heavy ? 0.6f : 0.4f, 0.07f, flashMat, false));
            StartCoroutine(Flash(pos, dir, heavy ? 3.6f : 2.2f, 0.06f, 0.18f, lineMat, true));
            int n = heavy ? 8 : 4;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = (dir + Random.insideUnitSphere * 0.45f + Vector3.up * Random.Range(-0.1f, 0.4f)).normalized;
                Emit(pos, d * Random.Range(5f, 10f), Random.Range(0.15f, 0.3f));
            }
            if (cam != null) NmjCameraKick.Get(cam).Kick(dir, heavy ? 0.12f : 0.05f);
            // 먹 튀김 (오카미 붓 베기 느낌): 검은 먹이 터지고, 강공격은 그 안에 주홍이 한 번 더
            StartCoroutine(Splat(pos, heavy ? 2.2f : 1.1f, new Color(0.05f, 0.05f, 0.06f, 1f), 0.32f));
            if (heavy) StartCoroutine(Splat(pos, 1.0f, new Color(0.85f, 0.18f, 0.08f, 1f), 0.22f));
            if (heavy && full) StartCoroutine(InkCut(pos));
        }

        private void OnParry(PlayerParry parry, bool success)
        {
            if (!success || NmjSwordSlash.Current != NmjSwordSlash.Mode.Flair || parry == null) return;
            Ensure();
            Vector3 pos = parry.transform.position + Vector3.up * 1.3f + parry.transform.forward * 0.7f;
            Camera cam = Camera.main;
            Vector3 right = cam != null ? cam.transform.right : parry.transform.right;
            HitStop(0.06f * DebugSlow);
            StartCoroutine(Flash(pos, right, 1.4f, 1.4f, 0.08f, flashMat, false));
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Vector3 d = (cam != null ? cam.transform.right * Mathf.Cos(a) + cam.transform.up * Mathf.Sin(a) : Random.onUnitSphere);
                Emit(pos, d * Random.Range(4f, 7f), Random.Range(0.12f, 0.25f));
            }
        }

        private void Emit(Vector3 pos, Vector3 vel, float life)
        {
            var p = new ParticleSystem.EmitParams { position = pos, velocity = vel, startSize = Random.Range(0.012f, 0.02f), startLifetime = life, startColor = Color.white, applyShapeToPosition = false };
            sparks.Emit(p, 1);
        }

        /// <summary>히트스톱: 잠깐 거의 멈췄다가 원래 시간 배율로 (다른 기능이 시간을 0으로 멈춘 중이면 건너뜀)</summary>
        private void HitStop(float seconds)
        {
            if (stopUntil < 0f)
            {
                if (Time.timeScale <= 0f) return;
                stopRestore = Time.timeScale;
                Time.timeScale = stopRestore * 0.05f;
            }
            stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + seconds);
            if (stopRoutine == null) stopRoutine = StartCoroutine(StopWait());
        }

        private IEnumerator StopWait()
        {
            while (Time.unscaledTime < stopUntil) yield return null;
            if (Time.timeScale > 0f) Time.timeScale = stopRestore;
            stopUntil = -1f;
            stopRoutine = null;
        }

        // 카메라를 보는 납작한 판. grow = 칼금 선처럼 길이가 늘었다가 가운데부터 옅어짐
        private IEnumerator Flash(Vector3 pos, Vector3 dir, float length, float width, float life, Material mat, bool grow)
        {
            life *= DebugSlow;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = grow ? "NMJ_SlashLine" : "NMJ_HitFlash";
            go.transform.SetParent(root, true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            float t = 0f;
            while (t < life)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 fwd = (pos - cam.transform.position).normalized;
                    Vector3 r = Vector3.ProjectOnPlane(dir, fwd).normalized;
                    if (r.sqrMagnitude < 1e-4f) r = cam.transform.right;
                    go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.Cross(fwd, r)));
                }
                float k = t / life;
                float len = grow ? length * Mathf.Min(1f, k / 0.25f) : length * Mathf.Lerp(0.6f, 1.15f, Mathf.Min(1f, k / 0.3f));
                float alpha = grow ? 1f - Mathf.SmoothStep(0.35f, 1f, k) : 1f - Mathf.SmoothStep(0.4f, 1f, k);
                go.transform.localScale = new Vector3(len, grow ? width * (1f - 0.6f * k) : width * (1f - 0.5f * k), 1f);
                mpb.SetColor("_Tint", grow ? new Color(1f, 0.55f, 0.35f, 1f) : new Color(1f, 0.92f, 0.8f, 1f));
                mr.SetPropertyBlock(mpb);
                SetVertexAlpha(go, alpha);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Destroy(go);
        }

        private static void SetVertexAlpha(GameObject go, float a)
        {
            var mf = go.GetComponent<MeshFilter>();
            Mesh m = mf.mesh;
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color(1f, 1f, 1f, a);
            m.colors = cols;
        }

        // 카메라를 보는 먹 튀김 한 장: 빠르게 퍼졌다가 가장자리부터 옅어짐
        private IEnumerator Splat(Vector3 pos, float size, Color tint, float life)
        {
            life *= DebugSlow;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "NMJ_InkSplat";
            go.transform.SetParent(root, true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = splatMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Tint", tint);
            mr.SetPropertyBlock(mpb);
            float roll = Random.Range(0f, 360f), t = 0f;
            while (t < life)
            {
                Camera cam = Camera.main;
                if (cam != null) go.transform.SetPositionAndRotation(pos - (pos - cam.transform.position).normalized * 0.05f, Quaternion.LookRotation(pos - cam.transform.position) * Quaternion.Euler(0f, 0f, roll));
                float k = t / life;
                go.transform.localScale = Vector3.one * size * Mathf.Lerp(0.5f, 1.1f, 1f - (1f - Mathf.Min(1f, k / 0.2f)) * (1f - Mathf.Min(1f, k / 0.2f)));
                SetVertexAlpha(go, 1f - Mathf.SmoothStep(0.3f, 1f, k));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Destroy(go);
        }

        // ───────── 안2 일섬: 종이·먹 반전 + 주홍 낙관 ─────────

        private IEnumerator InkCut(Vector3 hitPos)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "NMJ_InkCut";
            quad.transform.SetParent(cam.transform, false);
            float d = cam.nearClipPlane + 0.02f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            quad.transform.localPosition = new Vector3(0f, 0f, d);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(h * cam.aspect * 1.05f, h * 1.05f, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = cutMat;
            Vector3 sp = cam.WorldToViewportPoint(hitPos);
            cutMat.SetVector("_Hit", new Vector4(sp.x, sp.y, 0f, 0f));

            Seal(cam, hitPos);

            float t = 0f;
            while (t < 0.07f * DebugSlow) { cutMat.SetFloat("_Amount", 1f); t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < 0.08f * DebugSlow) { cutMat.SetFloat("_Amount", 1f - t / (0.08f * DebugSlow)); t += Time.unscaledDeltaTime; yield return null; }
            Destroy(quad);
        }

        // 맞은 자리에 주홍 낙관(討) — 찍히듯 작아지며 내려앉고, 잠깐 남았다가 옅어진다
        private void Seal(Camera cam, Vector3 hitPos)
        {
            if (glyphFont == null)
            {
                foreach (TMP_Text tt in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
                    if (tt.font != null && tt.font.name.Contains("HudGlyphs")) { glyphFont = tt.font; break; }
#if UNITY_EDITOR
                if (glyphFont == null) glyphFont = NmjMenuUi.FindGlyphFont();
#endif
            }
            var canvasGo = new GameObject("NMJ_Seal", typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(root, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 940;
            var group = canvasGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var box = new GameObject("Seal", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)box.transform;
            float s = Screen.height / 1080f;
            rt.sizeDelta = new Vector2(120f, 120f) * s;
            Vector3 sp = cam.WorldToScreenPoint(hitPos);
            rt.position = new Vector3(sp.x + 90f * s, sp.y + 60f * s, 0f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-8f, 8f));
            box.GetComponent<Image>().color = new Color(0.78f, 0.14f, 0.08f, 0.92f);

            if (glyphFont != null)
            {
                var txt = new GameObject("Glyph", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                txt.transform.SetParent(box.transform, false);
                ((RectTransform)txt.transform).sizeDelta = rt.sizeDelta;
                txt.font = glyphFont;
                txt.text = "討";
                txt.fontSize = 92f * s;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = new Color(0.98f, 0.94f, 0.86f);
            }
            StartCoroutine(SealAnim(rt, group, canvasGo));
        }

        private IEnumerator SealAnim(RectTransform rt, CanvasGroup group, GameObject go)
        {
            float t = 0f;
            float sl = DebugSlow;
            while (t < 0.9f * sl)
            {
                float k = Mathf.Min(1f, t / (0.08f * sl));
                rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, 1f - (1f - k) * (1f - k));
                group.alpha = t < 0.55f * sl ? Mathf.Min(1f, t / (0.04f * sl)) : 1f - (t - 0.55f * sl) / (0.35f * sl);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Destroy(go);
        }
    }

    /// <summary>카메라를 베는 방향으로 한 번 밀었다가 돌려놓는다 (NGH 카메라·흔들림 다음에 덧붙임)</summary>
    [DefaultExecutionOrder(1600)]
    public class NmjCameraKick : MonoBehaviour
    {
        private Vector3 dir;
        private float amount, t0 = -1f;
        private const float Dur = 0.14f;

        public static NmjCameraKick Get(Camera cam)
        {
            var k = cam.GetComponent<NmjCameraKick>();
            return k != null ? k : cam.gameObject.AddComponent<NmjCameraKick>();
        }

        public void Kick(Vector3 worldDir, float meters) { dir = worldDir.normalized; amount = meters; t0 = Time.unscaledTime; }

        private Vector3 applied;

        // 지난 프레임에 더한 만큼 먼저 뺀다 (카메라 위치를 매 프레임 다시 잡지 않는 카메라에서도 쌓이지 않게)
        private void Update()
        {
            transform.position -= applied;
            applied = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (t0 < 0f) return;
            float k = (Time.unscaledTime - t0) / (Dur * NmjSwordFlair.DebugSlow);
            if (k >= 1f) { t0 = -1f; return; }
            float push = k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f;   // 빨리 밀리고 천천히 돌아옴
            applied = dir * amount * push * push;
            transform.position += applied;
        }
    }
}
