#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 HUD 프리팹(chg_PlayerHUD) 만들기 — 처음 한 번은 자동, 이후 메뉴 CHG/Build Player HUD
///   좌하단: 체력바, 그 아래 증기 막대 (+ 개발용 표시 때 체력바 위에 수치) / 우하단: [회복약 칸] [무기 칸 x2] (+ 개발용 표시 때 'Tab' 안내)
/// 만든 뒤에는 프리팹을 열어 위치·크기·색을 직접 바꿔도 된다. (다시 만들면 초기화되므로 확인 창이 뜬다)
/// 글꼴: Fonts/NGH/ChosunCentennial_otf SDF (팀 공용 한글 글꼴)
/// </summary>
[InitializeOnLoad]
public static class chg_PlayerHUDSetup
{
    const string PrefabPath = "Assets/Prefabs/CHG/chg_PlayerHUD.prefab";
    const string FontPath = "Assets/Fonts/NGH/ChosunCentennial_otf SDF.asset";
    const string AutoKey = "chg_PlayerHUD_v2";   // v2: 무기 2칸, 회복약 글자, 개발용 수치·Tab 안내
    const string SteamKey = "chg_PlayerHUD_steam_v1";   // 기존 프리팹에 증기 막대 추가 + 수치를 체력바 위로 (다른 부분은 그대로)

    const float SteamBarHeight = 12f;
    const float SteamBarGap = 8f;

    // 색 (먹·한지·금테 느낌의 기본값)
    static readonly Color BorderColor = new Color(0.55f, 0.45f, 0.3f, 0.9f);
    static readonly Color BackColor = new Color(0f, 0f, 0f, 0.6f);
    static readonly Color TrailColor = new Color(0.95f, 0.9f, 0.8f, 1f);
    static readonly Color FillColor = new Color(0.72f, 0.13f, 0.11f, 1f);
    static readonly Color SteamFillColor = new Color(0.8f, 0.82f, 0.84f, 1f);
    static readonly Color SlotBorder = new Color(0.55f, 0.52f, 0.48f, 0.55f);
    static readonly Color SlotInner = new Color(0.05f, 0.05f, 0.05f, 0.55f);

    static chg_PlayerHUDSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorPrefs.GetBool(AutoKey, false))
            {
                EditorPrefs.SetBool(AutoKey, true);
                EditorPrefs.SetBool(SteamKey, true);   // 새로 만들면 증기 막대가 이미 들어 있음
                if (!File.Exists(PrefabPath)) { Build(); return; }
            }
            if (!EditorPrefs.GetBool(SteamKey, false))
            {
                EditorPrefs.SetBool(SteamKey, true);
                if (File.Exists(PrefabPath)) UpgradeSteamBar();
            }
        };
    }

    [MenuItem("CHG/Build Player HUD")]
    public static void BuildFromMenu()
    {
        if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("chg_PlayerHUD 다시 만들기",
                "프리팹을 새로 만들면 직접 바꾼 위치·크기·색이 초기화됩니다.\n계속할까요?", "다시 만들기", "취소"))
        {
            return;
        }
        Build();
    }

    static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (!font) Debug.LogWarning("[CHG] 한글 글꼴을 찾지 못했습니다 (기본 글꼴 사용): " + FontPath);

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            // 캔버스
            var root = new GameObject("chg_PlayerHUD", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;   // 적 체력바(-50)·패링 원(-40)보다 위
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            var hud = root.AddComponent<chg_PlayerHUD>();

            // ── 좌하단 체력바
            var hp = Rect("HpBar", root.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            hp.anchoredPosition = new Vector2(60f, 70f);
            hp.sizeDelta = new Vector2(440f, 20f);
            Img("Border", hp, BorderColor, 0f);
            Img("Back", hp, BackColor, 2f);
            var trail = Img("Trail", hp, TrailColor, 2f);
            var fill = Img("Fill", hp, FillColor, 2f);

            // 체력바 아래 증기 막대
            var steamFill = CreateSteamBar(root.transform, hp);

            // 개발용 표시(0 키)일 때만 보이는 체력·증기 수치 (체력바 위)
            var hpValue = Text("HpValue", root.transform, font, "HP 100 / 100    증기 100 / 100", 20f);
            PlaceValueText(hpValue, hp);
            hpValue.gameObject.SetActive(false);

            // ── 우하단: [회복약] [무기 칸들] — 오른쪽 정렬, 없는 무기 칸은 자동으로 빠짐
            var row = Rect("BottomRight", root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            row.anchoredPosition = new Vector2(-60f, 60f);
            row.sizeDelta = new Vector2(600f, 100f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerRight;
            layout.spacing = 12f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // 회복약 칸 (모양만, 비워 둠)
            var potion = Rect("Potion", row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            potion.sizeDelta = new Vector2(84f, 84f);
            Img("Border", potion, SlotBorder, 0f);
            Img("Inner", potion, SlotInner, 3f);
            var potionLabel = Text("Name", potion, font, "회복약", 22f);
            potionLabel.color = new Color(0.7f, 0.68f, 0.64f, 0.8f);

            var spacer = Rect("Gap", row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            spacer.sizeDelta = new Vector2(16f, 10f);

            // 무기 칸 2개 (기획: 무기 2개 장착)
            var slots = new chg_PlayerHUD.WeaponSlot[2];
            for (int i = 0; i < 2; i++)
            {
                var s = Rect("Weapon_" + i, row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                s.sizeDelta = new Vector2(100f, 100f);
                var border = Img("Border", s, SlotBorder, 0f);
                var inner = Img("Inner", s, SlotInner, 3f);
                var label = Text("Name", s, font, "무기", 28f);
                slots[i] = new chg_PlayerHUD.WeaponSlot { root = s.gameObject, border = border, inner = inner, label = label };
            }

            // 무기 칸 위 'Tab' 안내
            var hint = Text("TabHint", root.transform, font, "Tab", 20f);
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-60f, 166f);
            hintRect.sizeDelta = new Vector2(120f, 28f);
            hint.alignment = TextAlignmentOptions.BottomRight;
            hint.color = new Color(0.85f, 0.82f, 0.75f, 0.7f);
            hint.gameObject.SetActive(false);   // 개발용 표시(0 키)일 때만

            // 연결
            var so = new SerializedObject(hud);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("hpFill").objectReferenceValue = fill.rectTransform;
            so.FindProperty("hpTrail").objectReferenceValue = trail.rectTransform;
            so.FindProperty("hpValueText").objectReferenceValue = hpValue;
            so.FindProperty("steamFill").objectReferenceValue = steamFill;
            so.FindProperty("tabHint").objectReferenceValue = hint.gameObject;
            var arr = so.FindProperty("weaponSlots");
            arr.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("root").objectReferenceValue = slots[i].root;
                el.FindPropertyRelative("border").objectReferenceValue = slots[i].border;
                el.FindPropertyRelative("inner").objectReferenceValue = slots[i].inner;
                el.FindPropertyRelative("label").objectReferenceValue = slots[i].label;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[CHG][검증] chg_PlayerHUD 프리팹 생성 (" + PrefabPath + ") — 씬에 하나 놓으면 동작");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // 체력바 바로 아래, 같은 길이의 증기 막대. 채움(Fill) RectTransform 반환
    static RectTransform CreateSteamBar(Transform parent, RectTransform hp)
    {
        var steam = Rect("SteamBar", parent, hp.anchorMin, hp.anchorMax, hp.pivot);
        steam.anchoredPosition = hp.anchoredPosition - new Vector2(0f, SteamBarHeight + SteamBarGap);
        steam.sizeDelta = new Vector2(hp.sizeDelta.x, SteamBarHeight);
        steam.SetSiblingIndex(hp.GetSiblingIndex() + 1);
        Img("Border", steam, BorderColor, 0f);
        Img("Back", steam, BackColor, 2f);
        return Img("Fill", steam, SteamFillColor, 2f).rectTransform;
    }

    // 수치 글자를 체력바 위(왼쪽 정렬)에 둔다 — 레벨업으로 체력바가 길어져도 겹치지 않게
    static void PlaceValueText(TMP_Text text, RectTransform hp)
    {
        var rt = (RectTransform)text.transform;
        rt.anchorMin = rt.anchorMax = hp.anchorMin;
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = hp.anchoredPosition + new Vector2(0f, hp.sizeDelta.y + 6f);
        rt.sizeDelta = new Vector2(600f, 28f);
        text.alignment = TextAlignmentOptions.BottomLeft;
    }

    // 기존 프리팹 업그레이드: 증기 막대 추가 + 수치 위치 이동. 직접 바꾼 다른 위치·색은 그대로 둔다
    [MenuItem("CHG/Upgrade Player HUD (증기 막대)")]
    public static void UpgradeSteamBar()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var hud = root.GetComponent<chg_PlayerHUD>();
            var hp = root.transform.Find("HpBar") as RectTransform;
            if (hud == null || hp == null)
            {
                Debug.LogWarning("[CHG] chg_PlayerHUD 업그레이드 실패: HpBar 를 찾지 못했습니다. 메뉴 CHG/Build Player HUD 로 다시 만들어 주세요.");
                return;
            }
            var so = new SerializedObject(hud);
            if (root.transform.Find("SteamBar") == null)
            {
                so.FindProperty("steamFill").objectReferenceValue = CreateSteamBar(root.transform, hp);
            }
            var hpValue = so.FindProperty("hpValueText").objectReferenceValue as TMP_Text;
            if (hpValue != null) PlaceValueText(hpValue, hp);
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[CHG][검증] chg_PlayerHUD 업그레이드: 증기 막대 추가, 수치를 체력바 위로 이동");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        return rt;
    }

    // 부모를 꽉 채우는 단색 이미지 (inset: 안쪽 여백 px). 비율 막대는 anchorMax.x 로 길이 조절
    static Image Img(string name, Transform parent, Color color, float inset)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f));
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, string text, float size)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.color = Color.white;
        return t;
    }
}
#endif
