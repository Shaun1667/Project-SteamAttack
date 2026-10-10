using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 메인 퀘스트 "고종의 어명을 받아 현무를 토벌한다" 한 줄기와
    /// 거기에 쓰이는 아이템 / 데이터베이스 에셋을 한 번에 만들어 주는 에디터 도구.
    ///
    /// 내용은 HyeonmuWorld_Main 씬(1887 정월, 멈춘 도성)의 장소·인물에 맞춰져 있다.
    /// 메뉴: SteamAttack ▸ 메인 퀘스트 생성 (현무 토벌)
    /// 이미 있는 에셋은 새로 만들지 않고 값만 갱신한다.
    /// </summary>
    public static class HyeonmuQuestLineBuilder
    {
        private const string RootFolder = "Assets/GameData/NMJ";
        private const string ItemFolder = RootFolder + "/Items";
        private const string QuestFolder = RootFolder + "/Quests";

        // 씬의 NPC / 적 / 지역 오브젝트에 그대로 적어주는 아이디.
        public const string NpcGojong = "npc_gojong";                  // 건청궁 마당 · 어명의 순간
        public const string NpcGisulja = "npc_gigichang_gisulja";      // 삼청동 기기창 기술자
        public const string NpcBaekdongsu = "npc_baekdongsu";          // 백검 백동수
        public const string EnemyByeonibyeong = "enemy_byeonibyeong";  // 한기에 중독된 군졸
        public const string EnemyHeuksu = "enemy_heuksu_fanatic";      // 흑수회 광신도
        public const string BossHyeonmu = "boss_hyeonmu";              // 현무
        public const string LocationHeuksuAltar = "loc_heuksu_altar";  // 운종가 흑수회 제단
        public const string LocationGigichang = "loc_gigichang";       // 삼청동 기기창

        [MenuItem("SteamAttack/메인 퀘스트 생성 (현무 토벌)", false, 0)]
        public static void Build()
        {
            EnsureFolders();

            // ── 아이템 ────────────────────────────────────────
            var edict = CreateItem("quest_imperial_edict", "어명 교지(敎旨)", ItemType.Quest, ItemGrade.Unique, 1,
                "옥새가 찍힌 교지. 북장을 감은 현무를 베라는 어명이 적혀 있다. 종이에 서리가 앉아 있다.");

            var core = CreateItem("mat_firewater_core", "물불 동력핵", ItemType.Material, ItemGrade.Uncommon, 99,
                "건청궁 물불 증기 발전기에서 뽑아낸 동력핵. 아직 미지근하다. 한기 속에서 기계를 돌리는 유일한 불씨.");

            var ring = CreateItem("mat_armillary_ring", "혼천의 고리 조각", ItemType.Quest, ItemGrade.Rare, 9,
                "현무가 삼킨 시간이 엉겨 붙은 혼천의 조각. 손에 쥐면 하루 전 소리가 들린다.");

            var scale = CreateItem("mat_hyeonmu_scale", "현무 비늘", ItemType.Material, ItemGrade.Legendary, 9,
                "검고 두꺼운 비늘. 불을 대면 불이 얼고, 칼을 대면 칼이 식는다.");

            var tonic = CreateItem("con_ondol_tonic", "온돌 탕약", ItemType.Consumable, ItemGrade.Common, 20,
                "기기창에서 달인 탕약. 뼛속 한기를 잠시 밀어낸다.");

            var sabre = CreateItem("eq_baekgeom_hwando", "백검의 환도", ItemType.Equipment, ItemGrade.Unique, 1,
                "백동수가 쥐여 준 환도. 칼등의 증기관이 돌면 날이 얼지 않는다.");

            var plate = CreateItem("eq_hyeonmu_plate", "현무 비늘 갑주", ItemType.Equipment, ItemGrade.Legendary, 1,
                "현무의 비늘을 기기창에서 벼려 만든 갑주. 시간을 삼키는 한기 속에서도 숨이 붙어 있게 한다.");

            var itemDatabase = CreateOrLoad<ItemDatabase>(RootFolder + "/ItemDatabase.asset");
            RegisterAssets(itemDatabase, "items", edict, core, ring, scale, tonic, sabre, plate);

            // ── 메인 퀘스트 5장 ───────────────────────────────
            var mq1 = CreateQuest(
                "mq_01_royal_edict", "어명(御命)",
                "건청궁 마당으로 들라. 얼어붙은 전등 아래에서 황제가 기다린다.",
                NpcGojong,
                "보아라. 저것이 북장을 감고 도성의 시간을 삼키고 있다. 전등도, 사람도, 정월도 저기서 멈췄다.\n어명이다 — 현무를 베라.",
                "교지를 받들었으면 삼청동으로 가라. 기기창에 아직 불씨가 남아 있다.",
                new[]
                {
                    Objective(QuestObjectiveType.Talk, NpcGojong, 1, "건청궁 마당에서 고종의 어명을 받든다", null),
                },
                Reward(new[] { (edict, 1), (tonic, 2) }, exp: 100, gold: 500, extraSlots: 0),
                nextQuestId: "mq_02_gigichang_ember");

            var mq2 = CreateQuest(
                "mq_02_gigichang_ember", "기기창의 불씨",
                "폭주 후 멈춘 물불 발전기에서 동력핵을 수거해 기기창 기술자에게 가져간다.",
                NpcGisulja,
                "…손이 얼어서 말이 늦습니다. 동력핵 다섯이면 화로를 다시 돌립니다. 짐도 더 실을 수 있게 배낭을 손봐 드리지요.",
                "화로가 돌았습니다. 이제 한기 속에서도 사흘은 버팁니다.",
                new[]
                {
                    Objective(QuestObjectiveType.Reach, LocationGigichang, 1, "삼청동 기기창에 도달한다", null),
                    Objective(QuestObjectiveType.Collect, null, 5, "물불 동력핵 5개를 수거한다", core),
                    Objective(QuestObjectiveType.Talk, NpcGisulja, 1, "기기창 기술자와 대화한다", null),
                },
                Reward(new[] { (tonic, 3) }, exp: 200, gold: 300, extraSlots: 4),
                nextQuestId: "mq_03_black_water",
                prerequisites: new[] { "mq_01_royal_edict" });

            var mq3 = CreateQuest(
                "mq_03_black_water", "운종가의 흑수회",
                "저잣거리에 제단을 세우고 현무를 신으로 섬기는 자들이 있다. 제단부터 끊어라.",
                NpcBaekdongsu,
                "저것을 신으로 모시는 미친 자들이 운종가에 제단을 세웠소. 여덟만 베면 길이 열리오.",
                "제단이 꺼졌소. 이제 군졸들 차례요.",
                new[]
                {
                    Objective(QuestObjectiveType.Kill, EnemyHeuksu, 8, "흑수회 광신도를 8명 토벌한다", null),
                    Objective(QuestObjectiveType.Reach, LocationHeuksuAltar, 1, "운종가 흑수회 제단에 도달한다", null),
                },
                Reward(new[] { (tonic, 5), (core, 3) }, exp: 350, gold: 800, extraSlots: 0),
                nextQuestId: "mq_04_frozen_soldiers",
                prerequisites: new[] { "mq_02_gigichang_ember" });

            var mq4 = CreateQuest(
                "mq_04_frozen_soldiers", "한기에 중독된 군졸",
                "현무의 한기에 변이한 군졸들을 거두고, 그들이 삼킨 혼천의 조각을 되찾는다.",
                NpcBaekdongsu,
                "저들도 어제까지 우리 군졸이었소. …베는 것이 거두는 것이오. 몸속에 박힌 혼천의 조각을 셋만 가져오시오.",
                "조각 셋이면 놈이 삼킨 시간을 잠시 되돌릴 수 있소. 그때가 유일한 틈이오. 이 환도를 받으시오.",
                new[]
                {
                    Objective(QuestObjectiveType.Kill, EnemyByeonibyeong, 12, "변이병을 12명 거둔다", null),
                    Objective(QuestObjectiveType.Collect, null, 3, "혼천의 고리 조각 3개를 되찾는다", ring),
                    Objective(QuestObjectiveType.Talk, NpcBaekdongsu, 1, "백동수에게 조각을 넘긴다", null),
                },
                Reward(new[] { (sabre, 1) }, exp: 500, gold: 1000, extraSlots: 0),
                nextQuestId: "mq_05_slay_hyeonmu",
                prerequisites: new[] { "mq_03_black_water" });

            var mq5 = CreateQuest(
                "mq_05_slay_hyeonmu", "현무를 토벌하라",
                "어명의 끝. 북장을 감은 현무를 베고 건청궁으로 돌아가 복명한다.",
                NpcGojong,
                "가라. 짐의 이름으로, 멈춘 정월을 도로 흐르게 하라.",
                "…바람이 분다. 눈이 다시 내리는구나. 그 비늘로 벼린 갑주를 내리노라.",
                new[]
                {
                    Objective(QuestObjectiveType.Kill, BossHyeonmu, 1, "현무를 토벌한다", null),
                    Objective(QuestObjectiveType.Collect, null, 1, "현무 비늘을 확보한다", scale),
                    Objective(QuestObjectiveType.Talk, NpcGojong, 1, "건청궁으로 돌아가 복명한다", null),
                },
                Reward(new[] { (plate, 1) }, exp: 1500, gold: 5000, extraSlots: 6),
                nextQuestId: string.Empty,
                prerequisites: new[] { "mq_04_frozen_soldiers" });

            var questDatabase = CreateOrLoad<QuestDatabase>(RootFolder + "/QuestDatabase.asset");
            RegisterAssets(questDatabase, "quests", mq1, mq2, mq3, mq4, mq5);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(questDatabase);
            Debug.Log($"[SteamAttack] 메인 퀘스트 '고종의 어명 → 현무 토벌' 5장과 아이템 7종을 {RootFolder} 에 만들었다.");
        }

        // ── 생성 헬퍼 ─────────────────────────────────────────

        private static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets", "GameData");
            CreateFolderIfMissing("Assets/GameData", "NMJ");
            CreateFolderIfMissing(RootFolder, "Items");
            CreateFolderIfMissing(RootFolder, "Quests");
        }

        private static void CreateFolderIfMissing(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static ItemData CreateItem(string id, string displayName, ItemType type, ItemGrade grade,
            int maxStack, string description)
        {
            var item = CreateOrLoad<ItemData>($"{ItemFolder}/Item_{id}.asset");

            var so = new SerializedObject(item);
            so.FindProperty("itemId").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("description").stringValue = description;
            so.FindProperty("itemType").enumValueIndex = (int)type;
            so.FindProperty("grade").enumValueIndex = (int)grade;
            so.FindProperty("maxStack").intValue = maxStack;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(item);
            return item;
        }

        private static ObjectiveSpec Objective(QuestObjectiveType type, string targetId, int amount,
            string description, ItemData targetItem)
        {
            return new ObjectiveSpec
            {
                Type = type,
                TargetId = targetId ?? string.Empty,
                Amount = amount,
                Description = description,
                TargetItem = targetItem,
            };
        }

        private static RewardSpec Reward((ItemData item, int count)[] items, int exp, int gold, int extraSlots)
        {
            return new RewardSpec { Items = items, Exp = exp, Gold = gold, ExtraSlots = extraSlots };
        }

        private static QuestData CreateQuest(string questId, string title, string summary, string giverId,
            string startDialogue, string completeDialogue, ObjectiveSpec[] objectives, RewardSpec reward,
            string nextQuestId, string[] prerequisites = null)
        {
            var quest = CreateOrLoad<QuestData>($"{QuestFolder}/Quest_{questId}.asset");

            var so = new SerializedObject(quest);
            so.FindProperty("questId").stringValue = questId;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("category").enumValueIndex = (int)QuestCategory.Main;
            so.FindProperty("summary").stringValue = summary;
            so.FindProperty("questGiverId").stringValue = giverId;
            so.FindProperty("startDialogue").stringValue = startDialogue;
            so.FindProperty("completeDialogue").stringValue = completeDialogue;
            so.FindProperty("autoComplete").boolValue = false;
            so.FindProperty("consumeCollectedItems").boolValue = true;
            so.FindProperty("nextQuestId").stringValue = nextQuestId ?? string.Empty;

            var objectivesProp = so.FindProperty("objectives");
            objectivesProp.arraySize = objectives.Length;
            for (int i = 0; i < objectives.Length; i++)
            {
                var element = objectivesProp.GetArrayElementAtIndex(i);
                var spec = objectives[i];
                element.FindPropertyRelative("type").enumValueIndex = (int)spec.Type;
                element.FindPropertyRelative("targetId").stringValue = spec.TargetId;
                element.FindPropertyRelative("targetItem").objectReferenceValue = spec.TargetItem;
                element.FindPropertyRelative("description").stringValue = spec.Description;
                element.FindPropertyRelative("requiredAmount").intValue = spec.Amount;
            }

            var prereqProp = so.FindProperty("prerequisiteQuestIds");
            var prereqList = prerequisites ?? new string[0];
            prereqProp.arraySize = prereqList.Length;
            for (int i = 0; i < prereqList.Length; i++)
                prereqProp.GetArrayElementAtIndex(i).stringValue = prereqList[i];

            var rewardProp = so.FindProperty("reward");
            rewardProp.FindPropertyRelative("exp").intValue = reward.Exp;
            rewardProp.FindPropertyRelative("gold").intValue = reward.Gold;
            rewardProp.FindPropertyRelative("extraInventorySlots").intValue = reward.ExtraSlots;

            var rewardItemsProp = rewardProp.FindPropertyRelative("items");
            var rewardItems = reward.Items ?? System.Array.Empty<(ItemData item, int count)>();
            rewardItemsProp.arraySize = rewardItems.Length;
            for (int i = 0; i < rewardItems.Length; i++)
            {
                var element = rewardItemsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = rewardItems[i].item;
                element.FindPropertyRelative("count").intValue = rewardItems[i].count;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(quest);
            return quest;
        }

        /// <summary>데이터베이스 에셋의 리스트에 빠진 항목만 덧붙인다.</summary>
        private static void RegisterAssets(ScriptableObject database, string listFieldName, params Object[] assets)
        {
            var so = new SerializedObject(database);
            var listProp = so.FindProperty(listFieldName);

            var existing = new HashSet<Object>();
            for (int i = 0; i < listProp.arraySize; i++)
                existing.Add(listProp.GetArrayElementAtIndex(i).objectReferenceValue);

            foreach (var asset in assets)
            {
                if (asset == null || existing.Contains(asset)) continue;

                listProp.arraySize++;
                listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = asset;
                existing.Add(asset);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private struct ObjectiveSpec
        {
            public QuestObjectiveType Type;
            public string TargetId;
            public int Amount;
            public string Description;
            public ItemData TargetItem;
        }

        private struct RewardSpec
        {
            public (ItemData item, int count)[] Items;
            public int Exp;
            public int Gold;
            public int ExtraSlots;
        }
    }
}
