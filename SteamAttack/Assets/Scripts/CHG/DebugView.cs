using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CHG
{
    /// <summary>
    /// 개발용 표시 전체 켜기/끄기 — 플레이 중 숫자 0 키
    /// 켜면: 적 범위 원·공격 판정 상자·플레이어 연결선(Game 뷰와 Scene 뷰 모두), 원거리 조준 레이저, 테스트 키(1~7, Alt 불필요), 좌측 상단 기능 설명·대상 정보,
    ///       적에게 입힌 데미지 숫자
    /// 끄면(플레이 시작 기본값): 위 기능이 모두 꺼져 실제 플레이 화면과 같아진다.
    ///
    /// 플레이 중 선 그리기는 Gizmo가 아니라 카메라 렌더링이 끝난 뒤 GL로 직접 그리므로
    /// Game 뷰의 Gizmos 버튼과 관계없이 보인다. (새 오브젝트를 만들지 않음)
    /// 편집 중(플레이 안 할 때)에는 기존처럼 선택한 오브젝트의 Gizmo로 보인다.
    ///
    /// 사용하는 곳: EnemyAI (범위 원·조준 레이저·테스트 키 1~7·설명 상자), EnemyAttackHitbox (판정 상자),
    ///            EnemyHealth.AnyDamaged (데미지 숫자 — 이 파일 아래쪽 '데미지 숫자' 구역)
    /// </summary>
    public static class DebugView
    {
        /// <summary>개발용 표시가 켜져 있는지 (플레이 시작 시 꺼짐)</summary>
        public static bool Enabled { get; private set; }

        /// <summary>켜짐/꺼짐이 바뀔 때</summary>
        public static event Action<bool> Changed;

        /// <summary>Gizmo를 그려도 되는지: 편집 중에만 (플레이 중에는 GL 선 그리기를 사용)</summary>
        public static bool ShowGizmos => !Application.isPlaying;

        private static readonly List<Action> drawers = new List<Action>();
        private static Material lineMaterial;
        private static bool hooked;
        private static int lastPollFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Enabled = false;
            Changed = null;
            drawers.Clear();
            damageNumbers.Clear();
            runnerTemplate = null;
            runner = null;
            lastPollFrame = -1;
            if (hooked)
            {
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                hooked = false;
            }
        }

        public static void SetEnabled(bool on)
        {
            if (Enabled == on)
            {
                return;
            }
            Enabled = on;
            if (!on)
            {
                damageNumbers.Clear();   // 끄면 떠 있던 데미지 숫자도 바로 사라짐
            }
            Debug.Log("[chg_DebugView] 개발용 표시 " + (on ? "켜짐" : "꺼짐") + " (0 키)");
            Changed?.Invoke(on);
        }

        /// <summary>0 키 입력 확인. 여러 곳에서 불러도 한 프레임에 한 번만 처리한다.</summary>
        public static void PollToggleKey()
        {
            if (lastPollFrame == Time.frameCount)
            {
                return;
            }
            lastPollFrame = Time.frameCount;

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }
            if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame)
            {
                SetEnabled(!Enabled);
            }
        }

        // ───────────── 좌측 상단 설명 (OnGUI 안에서만 호출) ─────────────

        private static GUIStyle helpStyle;
        private static Texture2D helpBackground;

        /// <summary>
        /// 반투명 상자에 글자를 그림 (OnGUI 안에서만 호출). offset은 좌측 상단 기준 위치(화면 비율 반영 전),
        /// 그린 영역을 돌려주므로 옆에 다음 상자를 붙일 수 있다.
        /// </summary>
        public static Rect DrawBox(string text, Vector2 offset)
        {
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            if (helpStyle == null || helpBackground == null)
            {
                helpBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                helpBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                helpBackground.Apply();
                helpStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
                helpStyle.normal.textColor = Color.white;
                helpStyle.normal.background = helpBackground;
            }
            helpStyle.fontSize = Mathf.RoundToInt(15 * scale);
            int pad = Mathf.RoundToInt(10 * scale);
            helpStyle.padding = new RectOffset(pad, pad, pad, pad);

            GUIContent content = new GUIContent(text);
            Vector2 size = helpStyle.CalcSize(content);
            float margin = 12f * scale;
            Rect rect = new Rect(offset.x + margin, offset.y + margin, size.x, size.y);
            GUI.Label(rect, content, helpStyle);
            return rect;
        }

        // ───────────── 선 그리기 (플레이 중) ─────────────

        /// <summary>개발용 표시가 켜져 있을 때 매 카메라마다 호출될 그리기 함수 등록 (안에서 Line/Circle/WireBox 사용)</summary>
        public static void Register(Action drawer)
        {
            if (drawer == null || drawers.Contains(drawer))
            {
                return;
            }
            drawers.Add(drawer);
            if (!hooked)
            {
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                hooked = true;
            }
        }

        public static void Unregister(Action drawer)
        {
            drawers.Remove(drawer);
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!Enabled || drawers.Count == 0 || cam == null)
            {
                return;
            }
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)
            {
                return;
            }

            Material mat = GetLineMaterial();
            if (mat == null)
            {
                return;
            }

            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            GL.Begin(GL.LINES);
            for (int i = drawers.Count - 1; i >= 0; i--)
            {
                try
                {
                    drawers[i]?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    drawers.RemoveAt(i);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        private static Material GetLineMaterial()
        {
            if (lineMaterial != null)
            {
                return lineMaterial;
            }
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return null;
            }
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);   // 바닥·몸에 가려지지 않게 항상 위에 그림
            return lineMaterial;
        }

        /// <summary>선 하나 (그리기 함수 안에서만 사용)</summary>
        public static void Line(Vector3 a, Vector3 b, Color color)
        {
            GL.Color(color);
            GL.Vertex(a);
            GL.Vertex(b);
        }

        /// <summary>바닥에 눕힌 원 (그리기 함수 안에서만 사용)</summary>
        public static void Circle(Vector3 center, float radius, Color color, int segments = 48)
        {
            if (radius <= 0f)
            {
                return;
            }
            GL.Color(color);
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                GL.Vertex(prev);
                GL.Vertex(next);
                prev = next;
            }
        }

        /// <summary>회전된 상자 외곽선 (그리기 함수 안에서만 사용)</summary>
        public static void WireBox(Vector3 center, Quaternion rotation, Vector3 size, Color color)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c = new Vector3[8];
            int n = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                c[n++] = center + rotation * new Vector3(h.x * x, h.y * y, h.z * z);
            }
            // 0:(-,-,-) 1:(-,-,+) 2:(-,+,-) 3:(-,+,+) 4:(+,-,-) 5:(+,-,+) 6:(+,+,-) 7:(+,+,+)
            int[] e = { 0, 1, 0, 2, 0, 4, 1, 3, 1, 5, 2, 3, 2, 6, 3, 7, 4, 5, 4, 6, 5, 7, 6, 7 };
            GL.Color(color);
            for (int i = 0; i < e.Length; i += 2)
            {
                GL.Vertex(c[e[i]]);
                GL.Vertex(c[e[i + 1]]);
            }
        }
        // ───────────── 데미지 숫자 (적에게 입힌 피해) ─────────────
        // 적이 피해를 받을 때마다(근접·총·수류탄·패링 등) 맞은 적의 가슴 높이에 숫자가 떠올랐다가 1.5초 뒤 사라진다.
        // 개발용 표시가 켜져 있을 때만. 숫자는 OnGUI로 그리므로 글자 오브젝트를 만들지 않는다.

        private const float DamageLifeTime = 1.5f;      // 떠 있는 시간(초)
        private const float DamageFadeTime = 0.4f;      // 마지막에 흐려지는 시간(초)
        private const float DamageRiseHeight = 0.5f;    // 떠오르는 높이(m)
        private const float DamageBaseHeight = 1.4f;    // 적 발밑에서 숫자가 뜰 높이(m)
        private const float DamageSpread = 0.35f;       // 연타 때 겹치지 않도록 흩뿌리는 범위(m)
        private static readonly Color DamageColor = new Color(1f, 0.97f, 0.9f, 1f);

        private struct DamageNumber
        {
            public Transform target;
            public Vector3 offset;
            public string text;
            public float startTime;
        }

        private static readonly List<DamageNumber> damageNumbers = new List<DamageNumber>();
        private static GameObject runnerTemplate;
        private static Runner runner;
        private static GUIStyle damageStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookDamage()
        {
            EnemyHealth.AnyDamaged -= OnAnyDamaged;
            EnemyHealth.AnyDamaged += OnAnyDamaged;
        }

        private static void OnAnyDamaged(EnemyHealth health, float amount)
        {
            if (!Enabled || health == null)
            {
                return;
            }
            EnsureRunner();
            damageNumbers.Add(new DamageNumber
            {
                target = health.transform,
                offset = Vector3.up * DamageBaseHeight + new Vector3(
                    UnityEngine.Random.Range(-DamageSpread, DamageSpread),
                    UnityEngine.Random.Range(0f, DamageSpread * 0.5f),
                    UnityEngine.Random.Range(-DamageSpread, DamageSpread)),
                // 소수점이 있으면 한 자리까지 (예: 7.5), 아니면 정수
                text = Mathf.Approximately(amount, Mathf.Round(amount)) ? Mathf.RoundToInt(amount).ToString() : amount.ToString("0.#"),
                startTime = Time.unscaledTime,
            });
        }

        // OnGUI를 받을 오브젝트 하나 (처음 필요할 때 오브젝트 풀에서 꺼냄)
        private static void EnsureRunner()
        {
            if (runner != null && runner.isActiveAndEnabled)
            {
                return;
            }
            if (runnerTemplate == null)
            {
                runnerTemplate = new GameObject("chg_DebugViewRunner");
                runnerTemplate.SetActive(false);
                ObjectPool pool = ObjectPool.Instance;
                if (pool != null)
                {
                    runnerTemplate.transform.SetParent(pool.transform, false);
                }
                runnerTemplate.AddComponent<Runner>();
            }
            GameObject go = ObjectPool.Spawn(runnerTemplate, Vector3.zero, Quaternion.identity);
            runner = go != null ? go.GetComponent<Runner>() : null;
        }

        private static void DrawDamageNumbers()
        {
            if (!Enabled || damageNumbers.Count == 0)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (Event.current.type == EventType.Layout)
            {
                damageNumbers.RemoveAll(d => d.target == null || now - d.startTime >= DamageLifeTime);
                return;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            if (damageStyle == null)
            {
                damageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            }
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);

            foreach (DamageNumber d in damageNumbers)
            {
                float age = now - d.startTime;
                if (age >= DamageLifeTime)
                {
                    continue;
                }
                float t = age / DamageLifeTime;
                float rise = DamageRiseHeight * (1f - (1f - t) * (1f - t));   // 빠르게 떠올랐다 느려짐
                Vector3 screen = cam.WorldToScreenPoint(d.target.position + d.offset + Vector3.up * rise);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float punch = Mathf.Clamp01(1f - age / 0.15f);   // 처음 0.15초 살짝 크게 튀어나옴
                damageStyle.fontSize = Mathf.RoundToInt(28f * scale * (1f + 0.35f * punch));
                float alpha = Mathf.Clamp01((DamageLifeTime - age) / DamageFadeTime);

                var rect = new Rect(screen.x - 100f, Screen.height - screen.y - 30f, 200f, 60f);   // GUI는 위쪽이 0
                var content = new GUIContent(d.text);
                float o = Mathf.Max(1f, 1.5f * scale);
                damageStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);   // 테두리
                GUI.Label(new Rect(rect.x - o, rect.y, rect.width, rect.height), content, damageStyle);
                GUI.Label(new Rect(rect.x + o, rect.y, rect.width, rect.height), content, damageStyle);
                GUI.Label(new Rect(rect.x, rect.y - o, rect.width, rect.height), content, damageStyle);
                GUI.Label(new Rect(rect.x, rect.y + o, rect.width, rect.height), content, damageStyle);
                damageStyle.normal.textColor = new Color(DamageColor.r, DamageColor.g, DamageColor.b, alpha);
                GUI.Label(rect, content, damageStyle);
            }
        }

        // 정적 클래스는 OnGUI를 받을 수 없어서 대신 받아 주는 작은 컴포넌트
        private class Runner : MonoBehaviour
        {
            private void OnGUI()
            {
                DrawDamageNumbers();
            }
        }
    }
}
