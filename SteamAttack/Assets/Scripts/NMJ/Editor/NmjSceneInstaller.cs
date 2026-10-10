using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// 지금 열려 있는 씬에 인벤토리 / 퀘스트를 통째로 설치한다.
    ///  - GameSystems (PlayerInventory + QuestManager + QuestDebugTester)
    ///  - 인벤토리 창 UI, 퀘스트 트래커 UI, EventSystem
    ///  - 씬에 이미 있는 인물 / 장소 오브젝트에 퀘스트 컴포넌트 연결
    ///
    /// 메뉴: SteamAttack ▸ 현재 씬에 인벤토리·메인퀘스트 설치
    /// 여러 번 눌러도 이미 있는 것은 다시 만들지 않는다.
    /// </summary>
    public static class NmjSceneInstaller
    {
        private const string SlotPrefabPath = "Assets/Prefabs/NMJ/InventorySlot.prefab";
        private const string DataRoot = "Assets/GameData/NMJ";

        private static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.12f, 0.94f);
        private static readonly Color SlotColor = new Color(0.17f, 0.18f, 0.21f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.90f, 0.84f, 1f);
        private static readonly Color AccentColor = new Color(0.85f, 0.67f, 0.35f, 1f);

        [MenuItem("SteamAttack/현재 씬에 인벤토리·메인퀘스트 설치", false, 1)]
        public static void Install()
        {
            var questDatabase = AssetDatabase.LoadAssetAtPath<QuestDatabase>(DataRoot + "/QuestDatabase.asset");
            if (questDatabase == null)
            {
                EditorUtility.DisplayDialog("SteamAttack",
                    "먼저 'SteamAttack ▸ 메인 퀘스트 생성 (현무 토벌)' 을 실행해라.", "확인");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            var slotPrefab = LoadOrBuildSlotPrefab();

            var systems = Find("GameSystems") ?? new GameObject("GameSystems");

            var playerInventory = GetOrAdd<PlayerInventory>(systems);
            var questManager = GetOrAdd<QuestManager>(systems);
            var tester = GetOrAdd<QuestDebugTester>(systems);

            var managerSo = new SerializedObject(questManager);
            managerSo.FindProperty("database").objectReferenceValue = questDatabase;
            managerSo.FindProperty("startingQuest").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<QuestData>(DataRoot + "/Quests/Quest_mq_01_royal_edict.asset");
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            var testerSo = new SerializedObject(tester);
            testerSo.FindProperty("firewaterCore").objectReferenceValue = LoadItem("mat_firewater_core");
            testerSo.FindProperty("armillaryRing").objectReferenceValue = LoadItem("mat_armillary_ring");
            testerSo.FindProperty("hyeonmuScale").objectReferenceValue = LoadItem("mat_hyeonmu_scale");
            testerSo.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = Find("UI_NMJ_Canvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("UI_NMJ_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                BuildInventoryPanel(canvasGo.transform, playerInventory, slotPrefab);
                BuildQuestTracker(canvasGo.transform);
            }

            int wired = WireSceneObjects();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SteamAttack] '{scene.name}' 씬에 인벤토리·메인퀘스트를 설치했다. 퀘스트 연결된 오브젝트 {wired}개. " +
                      "재생 후 I 키로 인벤토리, F1~F11 로 메인 퀘스트를 진행할 수 있다.");
        }

        // ── 씬 오브젝트 연결 ──────────────────────────────────

        /// <summary>씬에 이미 있는 인물 / 적 / 장소 오브젝트에 퀘스트 컴포넌트를 붙인다.</summary>
        private static int WireSceneObjects()
        {
            int count = 0;

            count += WireGiver("어명의 순간", HyeonmuQuestLineBuilder.NpcGojong, "고종",
                "mq_01_royal_edict", "mq_05_slay_hyeonmu");
            count += WireGiver("기기창 기술자", HyeonmuQuestLineBuilder.NpcGisulja, "기기창 기술자",
                "mq_02_gigichang_ember");
            count += WireGiver("백동수", HyeonmuQuestLineBuilder.NpcBaekdongsu, "백검 백동수",
                "mq_03_black_water", "mq_04_frozen_soldiers");

            count += WireKillTarget("변이병", HyeonmuQuestLineBuilder.EnemyByeonibyeong);
            count += WireKillTarget("흑수회 광신도", HyeonmuQuestLineBuilder.EnemyHeuksu);
            count += WireKillTarget("현무 (玄武)", HyeonmuQuestLineBuilder.BossHyeonmu);

            count += WireZone("흑수회 제단", HyeonmuQuestLineBuilder.LocationHeuksuAltar, "흑수회 제단");
            count += WireZone("기기창 (機器廠)", HyeonmuQuestLineBuilder.LocationGigichang, "기기창");

            return count;
        }

        /// <summary>인물 본체(가장 바깥쪽 매치) 하나에만 붙인다.</summary>
        private static int WireGiver(string nameFragment, string npcId, string displayName, params string[] questIds)
        {
            var target = Outermost(FindAllByNameFragment(nameFragment));
            if (target == null) return 0;

            var giver = GetOrAdd<QuestGiverNpc>(target);
            var so = new SerializedObject(giver);
            so.FindProperty("npcId").stringValue = npcId;
            so.FindProperty("displayName").stringValue = displayName;

            var listProp = so.FindProperty("offeredQuests");
            listProp.arraySize = questIds.Length;
            for (int i = 0; i < questIds.Length; i++)
            {
                listProp.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<QuestData>($"{DataRoot}/Quests/Quest_{questIds[i]}.asset");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        /// <summary>개체 하나하나에 붙여야 하므로 말단 매치 전부에 붙인다(묶음 부모는 제외).</summary>
        private static int WireKillTarget(string nameFragment, string enemyId)
        {
            int count = 0;
            foreach (var target in LeavesOnly(FindAllByNameFragment(nameFragment)))
            {
                var reporter = GetOrAdd<QuestKillReporter>(target);
                var so = new SerializedObject(reporter);
                so.FindProperty("enemyId").stringValue = enemyId;
                so.ApplyModifiedPropertiesWithoutUndo();
                count++;
            }

            return count;
        }

        /// <summary>대상 오브젝트를 감싸는 트리거 영역을 QuestZones 아래에 만든다.</summary>
        private static int WireZone(string nameFragment, string locationId, string label)
        {
            var target = Outermost(FindAllByNameFragment(nameFragment));
            if (target == null) return 0;

            var zoneRoot = Find("QuestZones") ?? new GameObject("QuestZones");
            string zoneName = $"Zone_{label}";

            var existing = zoneRoot.transform.Find(zoneName);
            if (existing != null) return 1;

            var bounds = CalculateBounds(target);
            var zone = new GameObject(zoneName);
            zone.transform.SetParent(zoneRoot.transform, false);
            zone.transform.position = bounds.center;

            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.Max(bounds.size, new Vector3(8f, 8f, 8f));

            var trigger = zone.AddComponent<QuestZoneTrigger>();
            var so = new SerializedObject(trigger);
            so.FindProperty("locationId").stringValue = locationId;
            so.ApplyModifiedPropertiesWithoutUndo();

            return 1;
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 8f);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>이름에 fragment 가 들어간 오브젝트를 전부 찾는다. 카메라 시점 표식은 제외.</summary>
        private static List<GameObject> FindAllByNameFragment(string fragment)
        {
            var found = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.Contains(fragment)) continue;
                    if (IsUnder(t, "시점 표식")) continue;
                    found.Add(t.gameObject);
                }
            }

            return found;
        }

        private static bool IsUnder(Transform t, string ancestorNameFragment)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name.Contains(ancestorNameFragment)) return true;
            return false;
        }

        /// <summary>계층상 가장 바깥쪽(부모 쪽) 매치 하나.</summary>
        private static GameObject Outermost(List<GameObject> matches)
        {
            GameObject best = null;
            int bestDepth = int.MaxValue;

            foreach (var go in matches)
            {
                int depth = 0;
                for (var p = go.transform.parent; p != null; p = p.parent) depth++;
                if (depth >= bestDepth) continue;

                bestDepth = depth;
                best = go;
            }

            return best;
        }

        /// <summary>같은 이름의 자손이 또 있으면 묶음 부모로 보고 버린다.</summary>
        private static List<GameObject> LeavesOnly(List<GameObject> matches)
        {
            var leaves = new List<GameObject>();
            foreach (var go in matches)
            {
                bool hasMatchingDescendant = false;
                foreach (var other in matches)
                {
                    if (other == go) continue;
                    if (!other.transform.IsChildOf(go.transform)) continue;

                    hasMatchingDescendant = true;
                    break;
                }

                if (!hasMatchingDescendant) leaves.Add(go);
            }

            return leaves;
        }

        private static GameObject Find(string name)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root;

                var child = root.transform.Find(name);
                if (child != null) return child.gameObject;
            }

            return null;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        // ── 인벤토리 창 ───────────────────────────────────────

        private static void BuildInventoryPanel(Transform canvas, PlayerInventory playerInventory,
            InventorySlotUI slotPrefab)
        {
            var uiRoot = new GameObject("InventoryUI", typeof(RectTransform));
            uiRoot.transform.SetParent(canvas, false);
            Stretch(uiRoot.GetComponent<RectTransform>());

            var panel = CreateUIObject("Panel", uiRoot.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-48f, 0f);
            panelRect.sizeDelta = new Vector2(640f, 780f);
            AddImage(panel, PanelColor);

            var title = CreateText(panel.transform, "TitleText", "봇짐", 34, TextAlignmentOptions.Left, AccentColor);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(24f, -18f);
            titleRect.sizeDelta = new Vector2(240f, 44f);

            var capacity = CreateText(panel.transform, "CapacityText", "0 / 20 칸", 28, TextAlignmentOptions.Right, TextColor);
            var capacityRect = capacity.rectTransform;
            capacityRect.anchorMin = capacityRect.anchorMax = new Vector2(1f, 1f);
            capacityRect.pivot = new Vector2(1f, 1f);
            capacityRect.anchoredPosition = new Vector2(-24f, -18f);
            capacityRect.sizeDelta = new Vector2(280f, 44f);

            var grid = CreateUIObject("SlotGrid", panel.transform);
            var gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(1f, 1f);
            gridRect.pivot = new Vector2(0.5f, 1f);
            gridRect.anchoredPosition = new Vector2(0f, -76f);
            gridRect.sizeDelta = new Vector2(-32f, 500f);

            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(104f, 104f);
            layout.spacing = new Vector2(10f, 10f);
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 5;

            var info = CreateUIObject("InfoBox", panel.transform);
            var infoRect = info.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 0f);
            infoRect.pivot = new Vector2(0.5f, 0f);
            infoRect.anchoredPosition = new Vector2(0f, 76f);
            infoRect.sizeDelta = new Vector2(-32f, 180f);
            AddImage(info, new Color(0f, 0f, 0f, 0.35f));

            var itemName = CreateText(info.transform, "ItemNameText", string.Empty, 30, TextAlignmentOptions.TopLeft, AccentColor);
            var nameRect = itemName.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -12f);
            nameRect.sizeDelta = new Vector2(-32f, 40f);

            var itemDesc = CreateText(info.transform, "ItemDescriptionText", string.Empty, 24, TextAlignmentOptions.TopLeft, TextColor);
            var descRect = itemDesc.rectTransform;
            descRect.anchorMin = Vector2.zero;
            descRect.anchorMax = Vector2.one;
            descRect.pivot = new Vector2(0.5f, 0.5f);
            descRect.offsetMin = new Vector2(16f, 12f);
            descRect.offsetMax = new Vector2(-16f, -56f);

            var sortButton = CreateButton(panel.transform, "SortButton", "정리");
            var buttonRect = sortButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-24f, 18f);
            buttonRect.sizeDelta = new Vector2(140f, 48f);

            var dragIcon = CreateUIObject("DragIcon", uiRoot.transform);
            var dragRect = dragIcon.GetComponent<RectTransform>();
            dragRect.sizeDelta = new Vector2(88f, 88f);
            var dragImage = AddImage(dragIcon, new Color(1f, 1f, 1f, 0.85f));
            dragImage.raycastTarget = false;
            dragIcon.SetActive(false);

            var inventoryUI = uiRoot.AddComponent<InventoryUI>();
            var so = new SerializedObject(inventoryUI);
            so.FindProperty("playerInventory").objectReferenceValue = playerInventory;
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("slotParent").objectReferenceValue = gridRect;
            so.FindProperty("slotPrefab").objectReferenceValue = slotPrefab;
            so.FindProperty("capacityText").objectReferenceValue = capacity;
            so.FindProperty("itemNameText").objectReferenceValue = itemName;
            so.FindProperty("itemDescriptionText").objectReferenceValue = itemDesc;
            so.FindProperty("dragIcon").objectReferenceValue = dragRect;
            so.FindProperty("dragIconImage").objectReferenceValue = dragImage;
            so.FindProperty("sortButton").objectReferenceValue = sortButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false); // 시작할 때는 닫아 둔다. I 키로 연다.
        }

        // ── 퀘스트 트래커 ─────────────────────────────────────

        private static void BuildQuestTracker(Transform canvas)
        {
            var trackerRoot = new GameObject("QuestTrackerUI", typeof(RectTransform));
            trackerRoot.transform.SetParent(canvas, false);
            Stretch(trackerRoot.GetComponent<RectTransform>());

            var box = CreateUIObject("Box", trackerRoot.transform);
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 1f);
            boxRect.pivot = new Vector2(0f, 1f);
            boxRect.anchoredPosition = new Vector2(700f, -40f);
            boxRect.sizeDelta = new Vector2(560f, 240f);
            AddImage(box, new Color(0.06f, 0.07f, 0.09f, 0.78f));

            var title = CreateText(box.transform, "TitleText", string.Empty, 30, TextAlignmentOptions.TopLeft, AccentColor);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -14f);
            titleRect.sizeDelta = new Vector2(-32f, 40f);

            var objectives = CreateText(box.transform, "ObjectiveText", string.Empty, 24, TextAlignmentOptions.TopLeft, TextColor);
            var objRect = objectives.rectTransform;
            objRect.anchorMin = Vector2.zero;
            objRect.anchorMax = Vector2.one;
            objRect.pivot = new Vector2(0.5f, 0.5f);
            objRect.offsetMin = new Vector2(16f, 44f);
            objRect.offsetMax = new Vector2(-16f, -60f);

            var state = CreateText(box.transform, "StateText", string.Empty, 24, TextAlignmentOptions.BottomLeft, AccentColor);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = new Vector2(0f, 0f);
            stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(0.5f, 0f);
            stateRect.anchoredPosition = new Vector2(0f, 12f);
            stateRect.sizeDelta = new Vector2(-32f, 32f);

            var tracker = trackerRoot.AddComponent<QuestTrackerUI>();
            var so = new SerializedObject(tracker);
            so.FindProperty("root").objectReferenceValue = box;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("objectiveText").objectReferenceValue = objectives;
            so.FindProperty("stateText").objectReferenceValue = state;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 슬롯 프리팹 ───────────────────────────────────────

        private static InventorySlotUI LoadOrBuildSlotPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
            if (existing != null) return existing.GetComponent<InventorySlotUI>();

            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets/Prefabs", "NMJ");

            var root = CreateUIObject("InventorySlot", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(104f, 104f);
            AddImage(root, SlotColor);

            var icon = CreateUIObject("Icon", root.transform);
            var iconRect = icon.GetComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(76f, 76f);
            var iconImage = icon.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var gradeBar = CreateUIObject("GradeBar", root.transform);
            var gradeRect = gradeBar.GetComponent<RectTransform>();
            gradeRect.anchorMin = new Vector2(0f, 0f);
            gradeRect.anchorMax = new Vector2(1f, 0f);
            gradeRect.pivot = new Vector2(0.5f, 0f);
            gradeRect.sizeDelta = new Vector2(-12f, 6f);
            gradeRect.anchoredPosition = new Vector2(0f, 6f);
            var gradeImage = gradeBar.AddComponent<Image>();
            gradeImage.raycastTarget = false;

            var count = CreateText(root.transform, "CountText", string.Empty, 22, TextAlignmentOptions.BottomRight, TextColor);
            var countRect = count.rectTransform;
            countRect.anchorMin = Vector2.zero;
            countRect.anchorMax = Vector2.one;
            countRect.offsetMin = new Vector2(4f, 10f);
            countRect.offsetMax = new Vector2(-8f, -4f);
            count.raycastTarget = false;

            var slotUI = root.AddComponent<InventorySlotUI>();
            var so = new SerializedObject(slotUI);
            so.FindProperty("iconImage").objectReferenceValue = iconImage;
            so.FindProperty("gradeFrame").objectReferenceValue = gradeImage;
            so.FindProperty("countText").objectReferenceValue = count;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(SlotPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath);
            Object.DestroyImmediate(root);

            return prefab.GetComponent<InventorySlotUI>();
        }

        // ── 공용 헬퍼 ─────────────────────────────────────────

        private static ItemData LoadItem(string id)
        {
            return AssetDatabase.LoadAssetAtPath<ItemData>($"{DataRoot}/Items/Item_{id}.asset");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string text, int size,
            TextAlignmentOptions alignment, Color color)
        {
            var go = CreateUIObject(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            var go = CreateUIObject(name, parent);
            AddImage(go, new Color(0.24f, 0.25f, 0.29f, 1f));

            var button = go.AddComponent<Button>();

            var text = CreateText(go.transform, "Label", label, 26, TextAlignmentOptions.Center, TextColor);
            Stretch(text.rectTransform);
            text.raycastTarget = false;

            return button;
        }
    }
}
