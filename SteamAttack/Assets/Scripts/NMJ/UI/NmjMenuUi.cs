using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// NMJ 화면 UI(ESC 메뉴·타이틀·보스 처치 문구)가 같이 쓰는 도우미.
    /// 흑 바탕 + 놋쇠 겹테 판, 고르면 먹 밑줄이 그어지는 글자 버튼, 인주 도장 텍스처를 코드로 만든다.
    /// </summary>
    public static class NmjMenuUi
    {
        // 오방색 (기획안 색 규칙)
        public static readonly Color Black = new Color(0.06f, 0.06f, 0.07f, 1f);    // 흑 #0F0F12
        public static readonly Color White = new Color(0.96f, 0.95f, 0.92f, 1f);    // 백 #F5F2EB
        public static readonly Color Brass = new Color(0.94f, 0.73f, 0.16f, 1f);    // 황 #F0BA29 (놋쇠)
        public static readonly Color Red = new Color(0.82f, 0.18f, 0.10f, 1f);      // 적 #D12E1A

        /// <summary>글자 버튼 모양</summary>
        public class Style
        {
            public TMP_FontAsset font;
            public Color textColor = White;
            public Color selectedColor = Brass;
            public Color underlineColor = Red;
            public float disabledAlpha = 0.35f;
            public float buttonHeight = 66f;
            public float fontSize = 32f;
        }

        /// <summary>글자 버튼 하나 (고르면 먹 밑줄)</summary>
        public class MenuButton
        {
            public Button button;
            public TMP_Text label;
            public RectTransform underline;
            public GameObject soonTag;
            public bool enabled = true;
            public float line;
        }

        // ───────── 캔버스 ─────────

        public static Canvas NewCanvas(string name, Transform parent, int sortingOrder, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            group = go.AddComponent<CanvasGroup>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ───────── 기본 조각 ─────────

        /// <summary>부모를 꽉 채우는 RectTransform</summary>
        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image NewImage(string name, Transform parent, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = NewRect(name, parent);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static RawImage NewRawImage(string name, Transform parent, Texture tex)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        public static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, string text, float size, Color color)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>가운데에 놓인 흑 바탕 + 놋쇠 겹테 판</summary>
        public static RectTransform Panel(Transform parent, Vector2 size, Color background, Color brass)
        {
            RectTransform panel = NewRect("Panel", parent);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = size;
            Image bg = NewImage("Bg", panel, background, Vector2.zero, Vector2.zero);
            bg.raycastTarget = true;
            Frame(panel, brass, 0f, 2f, 1f);
            Frame(panel, brass, 8f, 1f, 0.45f);
            return panel;
        }

        /// <summary>안쪽으로 inset 만큼 들어간 사각 테두리</summary>
        public static void Frame(RectTransform parent, Color color, float inset, float thickness, float alpha)
        {
            Color c = new Color(color.r, color.g, color.b, alpha);
            Edge(parent, c, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(inset, -inset - thickness), new Vector2(-inset, -inset));
            Edge(parent, c, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(inset, inset), new Vector2(-inset, inset + thickness));
            Edge(parent, c, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(inset, inset), new Vector2(inset + thickness, -inset));
            Edge(parent, c, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-inset - thickness, inset), new Vector2(-inset, -inset));
        }

        private static void Edge(RectTransform parent, Color c, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            Image e = NewImage("Edge", parent, c, offMin, offMax);
            e.rectTransform.anchorMin = min;
            e.rectTransform.anchorMax = max;
        }

        /// <summary>판 아래쪽 작은 안내 글</summary>
        public static TMP_Text Hint(RectTransform parent, TMP_FontAsset font, string text, Color color)
        {
            TMP_Text h = NewText("Hint", parent, font, text, 18f, color);
            RectTransform rt = h.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 30f);
            rt.anchoredPosition = new Vector2(0f, 22f);
            h.alpha = 0.5f;
            return h;
        }

        // ───────── 글자 버튼 ─────────

        public static MenuButton AddButton(List<MenuButton> list, RectTransform parent, Style style, string text, UnityAction onClick)
        {
            var b = new MenuButton();
            RectTransform rt = NewRect("Btn_" + text, parent);
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;   // 누를 자리만 잡는다
            b.button = rt.gameObject.AddComponent<Button>();
            b.button.targetGraphic = hit;
            b.button.transition = Selectable.Transition.None;
            b.button.onClick.AddListener(onClick);

            // 마우스를 올리면 그 줄을 고른다 → 키보드와 마우스가 같은 표시를 쓴다
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (b.enabled && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(rt.gameObject); });
            trigger.triggers.Add(enter);

            b.label = NewText("Label", rt, style.font, text, style.fontSize, style.textColor);

            b.underline = NewRect("Underline", rt);
            b.underline.anchorMin = b.underline.anchorMax = new Vector2(0.5f, 0.5f);
            b.underline.sizeDelta = new Vector2(0f, 3f);
            b.underline.anchoredPosition = new Vector2(0f, -style.fontSize * 0.62f);
            Image ink = b.underline.gameObject.AddComponent<Image>();
            ink.color = style.underlineColor;
            ink.raycastTarget = false;

            TMP_Text soon = NewText("Soon", rt, style.font, "준비 중", 16f, style.textColor);
            soon.alignment = TextAlignmentOptions.Right;
            soon.alpha = 0.5f;
            b.soonTag = soon.gameObject;
            b.soonTag.SetActive(false);

            list.Add(b);
            return b;
        }

        /// <summary>쓸 수 없는 버튼은 흐리게 '준비 중'으로</summary>
        public static void SetEnabled(MenuButton b, bool on, Style style)
        {
            b.enabled = on;
            b.button.interactable = on;
            b.label.alpha = on ? 1f : style.disabledAlpha;
            b.soonTag.SetActive(!on);
        }

        /// <summary>버튼을 위에서부터 한 줄씩 둔다</summary>
        public static void LayoutColumn(List<MenuButton> buttons, float height)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = (RectTransform)buttons[i].button.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, height);
                rt.anchoredPosition = new Vector2(0f, -i * height);
            }
            LinkColumn(buttons);
        }

        /// <summary>위아래 키로 쓸 수 있는 버튼만 돌아가며 오가게 잇는다 ('준비 중'은 건너뜀)</summary>
        public static void LinkColumn(List<MenuButton> buttons)
        {
            var usable = buttons.FindAll(b => b.enabled);
            foreach (MenuButton b in buttons) b.button.navigation = new Navigation { mode = Navigation.Mode.None };
            for (int i = 0; i < usable.Count; i++)
            {
                usable[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = usable[(i - 1 + usable.Count) % usable.Count].button,
                    selectOnDown = usable[(i + 1) % usable.Count].button,
                };
            }
        }

        /// <summary>고른 줄은 먹 밑줄이 왼쪽에서 오른쪽으로 그어지고 글자가 놋쇠색이 된다 (멈춘 시간에도 움직임)</summary>
        public static void UpdateButtons(List<MenuButton> buttons, Style style)
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            float dt = Time.unscaledDeltaTime;
            foreach (MenuButton b in buttons)
            {
                bool on = b.enabled && selected == b.button.gameObject;
                b.line = Mathf.MoveTowards(b.line, on ? 1f : 0f, dt * 7f);
                float width = b.label.preferredWidth + 24f;
                b.underline.sizeDelta = new Vector2(width * Mathf.SmoothStep(0f, 1f, b.line), b.underline.sizeDelta.y);
                Color c = on ? style.selectedColor : style.textColor;
                c.a = b.enabled ? 1f : style.disabledAlpha;
                b.label.color = c;
            }
        }

        public static void Select(MenuButton b)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b != null ? b.button.gameObject : null);
        }

        // ───────── 먹·인주 텍스처 ─────────

        /// <summary>붉은 도장: 거친 둘레, 고르지 않은 인주, 군데군데 끊긴 새김 테두리</summary>
        public static Texture2D DrawSeal(int S, Color sealColor, Color paperColor, float seed)
        {
            var px = new Color32[S * S];
            float cx = S * 0.5f;
            float inset = S * 0.06f, corner = S * 0.10f;
            float hx = (S - 2f * inset) * 0.5f - corner;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - cx) - hx;
                    float qy = Mathf.Abs(y + 0.5f - cx) - hx;
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - corner;
                    outside += (Mathf.PerlinNoise(x * 0.18f + seed, y * 0.18f) - 0.5f) * S * 0.05f;
                    float a = Mathf.Clamp01(0.5f - outside);
                    if (a <= 0f) { px[y * S + x] = new Color32(0, 0, 0, 0); continue; }

                    float depth = -outside;
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(depth - S * 0.085f) / (S * 0.022f));
                    if (Mathf.PerlinNoise(x * 0.12f, y * 0.12f + 40f + seed) > 0.68f) ring = 0f;
                    float inkN = Mathf.PerlinNoise(x * 0.06f + 10f, y * 0.06f + seed);
                    float speck = Mathf.PerlinNoise(x * 0.45f + 5f, y * 0.45f + 9f + seed);
                    float paperMix = Mathf.Max(ring, speck > 0.74f ? (speck - 0.74f) / 0.26f * 0.9f : 0f);

                    Color c = Color.Lerp(sealColor * (0.88f + 0.2f * inkN), paperColor, paperMix);
                    c.a = a;
                    px[y * S + x] = c;
                }
            }
            return MakeTexture("NMJ_Seal", S, S, px);
        }

        /// <summary>
        /// 가로로 그은 굵은 먹 띠: 가운데가 짙고 위아래 둘레가 붓결처럼 거칠며, 붓이 마른 쪽(오른쪽 끝)은 결이 갈라진다.
        /// </summary>
        public static Texture2D DrawBrushBand(int W, int H, Color ink, float seed)
        {
            var px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / (W - 1);
                // 붓을 대는 왼쪽은 둥글게 시작, 오른쪽은 가늘어지며 끝난다
                float thick = Mathf.Lerp(0.42f, 0.30f, u) * Mathf.Clamp01(u * 14f) * Mathf.Clamp01((1f - u) * 6f + 0.15f);
                float centre = 0.5f + (Mathf.PerlinNoise(u * 2.5f + seed, 3f) - 0.5f) * 0.10f;
                float dry = Mathf.Clamp01((u - 0.55f) / 0.45f);   // 오른쪽으로 갈수록 마른 붓
                for (int y = 0; y < H; y++)
                {
                    float v = (float)y / (H - 1);
                    float edge = thick * (0.85f + 0.3f * Mathf.PerlinNoise(u * 18f + seed, v * 3f));
                    float d = Mathf.Abs(v - centre);
                    float a = Mathf.Clamp01((edge - d) * H * 0.6f);
                    // 붓털 결: 가로로 긴 줄무늬가 마른 쪽에서 갈라진다
                    float streak = Mathf.PerlinNoise(u * 3f + seed, v * 60f);
                    a *= Mathf.Lerp(1f, Mathf.SmoothStep(0.25f, 0.6f, streak), dry * 0.9f);
                    a *= 0.92f + 0.08f * Mathf.PerlinNoise(u * 30f, v * 30f + seed);
                    Color c = ink;
                    c.a = a * ink.a;
                    px[y * W + x] = c;
                }
            }
            return MakeTexture("NMJ_BrushBand", W, H, px);
        }

        private static Texture2D MakeTexture(string name, int w, int h, Color32[] px)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 ChosunCentennial_otf SDF(한자는 대체 글꼴로 나옴)를 찾는다</summary>
        public static TMP_FontAsset FindDefaultFont()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("ChosunCentennial_otf SDF t:TMP_FontAsset"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Fallback")) continue;
                var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) return font;
            }
            return null;
        }

        /// <summary>
        /// 화면 한자(玄武·命·鎭 등) 전용 고정 폰트. 조선 폰트에는 한자가 거의 없고, 대신 채워 주는 NGH 동적 폰트는
        /// 아틀라스가 비면 글자가 안 보이므로 한자만 담은 고정 폰트를 따로 쓴다. 글자를 더하려면 NmjHudFontBuilder.
        /// </summary>
        public static TMP_FontAsset FindGlyphFont()
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NMJ/NMJ_HudGlyphs SDF.asset");
        }
#endif
    }
}
