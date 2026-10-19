using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 화면 자막·안내 문구 (한 번에 하나).
    ///
    ///   NmjSubtitle.Caption("한 호흡에, 한 점에");                  ← 화면 아래쪽 붓글씨 자막
    ///   NmjSubtitle.Key("CTRL", "받아치기");                         ← 화면 가운데 아래, 키 패 + 설명
    ///   NmjSubtitle.Key("Q", "피할 수 없다 — 되감아라", 2.5f, true);  ← 붉은 경고 (시간 역행 전용 공격 직전)
    ///
    /// 처음 부를 때 그 씬에 캔버스를 만든다. 멈춘 시간(슬로 모션·ESC)과 관계없이 흐른다.
    /// 다른 담당(BHS 보스 패턴 등)에서도 이 두 함수만 부르면 된다.
    /// </summary>
    public class NmjSubtitle : MonoBehaviour
    {
        private static NmjSubtitle instance;

        private CanvasGroup group;
        private RectTransform captionRoot, keyRoot;
        private TMP_Text captionText, keyGlyph, keyText;
        private Image keyBox, keyBand;
        private float timer, duration, alpha;
        private bool keyMode;
        private TMP_FontAsset font;

        private static readonly Color Ink = new Color(0.04f, 0.04f, 0.05f, 0.82f);

        /// <summary>화면 아래쪽 자막</summary>
        public static void Caption(string text, float seconds = 3.5f) => Get()?.Show(false, null, text, seconds, false);

        /// <summary>키 안내 (warning = 붉은 경고)</summary>
        public static void Key(string key, string text, float seconds = 3f, bool warning = false) => Get()?.Show(true, key, text, seconds, warning);

        private static NmjSubtitle Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("NMJ_Subtitle");
            instance = go.AddComponent<NmjSubtitle>();
            instance.Build();
            return instance;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Show(bool asKey, string key, string text, float seconds, bool warning)
        {
            keyMode = asKey;
            captionRoot.gameObject.SetActive(!asKey);
            keyRoot.gameObject.SetActive(asKey);
            if (asKey)
            {
                keyGlyph.text = key;
                keyText.text = text;
                keyBox.color = warning ? Red : White;
                keyGlyph.color = warning ? White : Black;
                keyText.color = warning ? new Color(1f, 0.75f, 0.68f, 1f) : White;
                // 키 패 폭을 글자에 맞춘다
                float w = Mathf.Max(64f, keyGlyph.GetPreferredValues(key).x + 34f);
                keyBox.rectTransform.sizeDelta = new Vector2(w, 64f);
            }
            else captionText.text = text;
            timer = 0f;
            duration = Mathf.Max(0.5f, seconds);
        }

        private void Update()
        {
            if (duration <= 0f) return;
            timer += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            // 0.25초 들어오고, 끝 0.6초 동안 사라진다
            float target = timer < duration - 0.6f ? 1f : Mathf.Clamp01((duration - timer) / 0.6f);
            alpha = timer < 0.25f ? Mathf.Clamp01(timer / 0.25f) : target;
            group.alpha = alpha;
            if (keyMode) keyRoot.anchoredPosition = new Vector2(0f, Mathf.Lerp(-12f, 0f, Mathf.Clamp01(timer / 0.25f)));
            if (timer >= duration) { duration = 0f; group.alpha = 0f; }
        }

        private void Build()
        {
#if UNITY_EDITOR
            font = FindDefaultFont();
#else
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f.name == "ChosunCentennial_otf SDF") { font = f; break; }
#endif
            Canvas canvas = NewCanvas("Canvas", transform, 70, out group);   // HUD 위, 처치 문구(80) 아래
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // 아래쪽 자막: 옅은 먹 띠 위에 백색 글자
            captionRoot = NewRect("Caption", canvas.transform);
            captionRoot.anchorMin = captionRoot.anchorMax = new Vector2(0.5f, 0.16f);
            captionRoot.sizeDelta = new Vector2(1100f, 80f);
            Image band = NewImage("Band", captionRoot, new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.zero);
            band.rectTransform.anchorMin = new Vector2(0.1f, 0.15f);
            band.rectTransform.anchorMax = new Vector2(0.9f, 0.85f);
            captionText = NewText("Text", captionRoot, font, "", 40f, White);
            captionText.characterSpacing = 12f;

            // 키 안내: 한지 패에 키 글자 + 오른쪽 설명
            keyRoot = NewRect("Key", canvas.transform);
            keyRoot.anchorMin = keyRoot.anchorMax = new Vector2(0.5f, 0.3f);
            keyRoot.sizeDelta = new Vector2(900f, 90f);
            keyBand = NewImage("Band", keyRoot, Ink, Vector2.zero, Vector2.zero);
            keyBand.rectTransform.anchorMin = new Vector2(0.12f, 0.1f);
            keyBand.rectTransform.anchorMax = new Vector2(0.88f, 0.9f);
            keyBox = NewImage("KeyBox", keyRoot, White, Vector2.zero, Vector2.zero);
            RectTransform kb = keyBox.rectTransform;
            kb.anchorMin = kb.anchorMax = new Vector2(0.5f, 0.5f);
            kb.pivot = new Vector2(1f, 0.5f);
            kb.anchoredPosition = new Vector2(-12f, 0f);
            keyGlyph = NewText("Glyph", kb, font, "", 32f, Black);
            keyText = NewText("Text", keyRoot, font, "", 32f, White);
            RectTransform kt = keyText.rectTransform;
            kt.anchorMin = kt.anchorMax = new Vector2(0.5f, 0.5f);
            kt.pivot = new Vector2(0f, 0.5f);
            kt.sizeDelta = new Vector2(420f, 64f);
            kt.anchoredPosition = new Vector2(12f, 0f);
            keyText.alignment = TextAlignmentOptions.Left;
        }
    }
}
