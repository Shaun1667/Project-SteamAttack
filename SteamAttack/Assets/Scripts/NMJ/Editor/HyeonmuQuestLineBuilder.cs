using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 메인 퀘스트 "고종의 어명을 받아 현무를 다시 잠재운다" 한 줄기와 퀘스트 데이터베이스를 한 번에 만들어 주는 에디터 도구.
    /// (인벤토리·아이템 삭제 2026-10-23: 예전 '아이템 모으기' 목표는 같은 아이디의 커스텀 보고 목표, 아이템 보상은 없음)
    ///
    /// 내용은 HyeonmuWorld_Main 씬(1887년 3월, 멈춘 도성)의 장소·인물에 맞춰져 있다.
    /// 메뉴: SteamAttack ▸ 메인 퀘스트 생성 (현무 토벌)
    /// 이미 있는 에셋은 새로 만들지 않고 값만 갱신한다.
    /// </summary>
    public static class HyeonmuQuestLineBuilder
    {
        private const string RootFolder = "Assets/GameData/NMJ";
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

            // 예전 아이템 아이디 — 커스텀 목표 아이디로 그대로 쓴다 (QuestDebugTester F6·F7·F10)
            const string core = "mat_firewater_core", ring = "mat_armillary_ring", scale = "mat_hyeonmu_scale";

            // ── 메인 퀘스트 5장 ───────────────────────────────
            var mq1 = CreateQuest(
                "mq_01_royal_edict", "어명(御命)",
                "건청궁 마당으로 들라. 얼어붙은 전등 아래에서 임금이 기다린다.",
                NpcGojong,
                "보아라. 저것이 북장을 감고 도성의 시간을 삼키고 있다. 전등도, 사람도, 봄눈도 저기서 멈췄다. 어명이다 — 가서, 저것을 다시 잠재우라. 죽이라는 것이 아니다.",
                "교지를 받들었으면 삼청동으로 가라. 기기창에 아직 불씨가 남아 있다.",
                new[]
                {
                    Objective(QuestObjectiveType.Talk, NpcGojong, 1, "건청궁 마당에서 고종의 어명을 받든다"),
                },
                Reward(exp: 100, gold: 500),
                nextQuestId: "mq_02_gigichang_ember");

            var mq2 = CreateQuest(
                "mq_02_gigichang_ember", "기기창의 불씨",
                "폭주 후 멈춘 물불 발전기에서 동력핵을 수거해 기기창 기술자에게 가져간다.",
                NpcGisulja,
                "…손이 얼어서 말이 늦습니다. 동력핵 다섯이면 화로를 다시 돌립니다. 짐도 더 실을 수 있게 배낭을 손봐 드리지요.",
                "화로가 돌았습니다. 이제 한기 속에서도 사흘은 버팁니다.",
                new[]
                {
                    Objective(QuestObjectiveType.Reach, LocationGigichang, 1, "삼청동 기기창에 도달한다"),
                    Objective(QuestObjectiveType.Custom, core, 5, "물불 동력핵 5개를 수거한다"),
                    Objective(QuestObjectiveType.Talk, NpcGisulja, 1, "기기창 기술자와 대화한다"),
                },
                Reward(exp: 200, gold: 300),
                nextQuestId: "mq_03_black_water",
                prerequisites: new[] { "mq_01_royal_edict" });

            var mq3 = CreateQuest(
                "mq_03_black_water", "운종가의 흑수회",
                "저잣거리에 제단을 세우고 현무를 신으로 섬기는 자들이 있다. 제단부터 끊어라.",
                NpcBaekdongsu,
                "저것을 신으로 모시는 겁먹은 자들이 운종가에 제단을 세웠소. 여덟만 잠재우면 길이 열리오.",
                "제단이 꺼졌소. 이제 군졸들 차례요.",
                new[]
                {
                    Objective(QuestObjectiveType.Kill, EnemyHeuksu, 8, "흑수회 광신도 8명을 잠재운다"),
                    Objective(QuestObjectiveType.Reach, LocationHeuksuAltar, 1, "운종가 흑수회 제단에 도달한다"),
                },
                Reward(exp: 350, gold: 800),
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
                    Objective(QuestObjectiveType.Kill, EnemyByeonibyeong, 12, "변이병을 12명 거둔다"),
                    Objective(QuestObjectiveType.Custom, ring, 3, "혼천의 고리 조각 3개를 되찾는다"),
                    Objective(QuestObjectiveType.Talk, NpcBaekdongsu, 1, "백동수에게 조각을 넘긴다"),
                },
                Reward(exp: 500, gold: 1000),
                nextQuestId: "mq_05_slay_hyeonmu",
                prerequisites: new[] { "mq_03_black_water" });

            var mq5 = CreateQuest(
                "mq_05_slay_hyeonmu", "현무를 잠재우라",
                "어명의 끝. 북장을 감은 현무를 다시 잠재우고 건청궁으로 돌아가 복명한다.",
                NpcGojong,
                "가라. 짐의 이름으로, 멈춘 봄을 도로 흐르게 하라.",
                "…바람이 분다. 눈이 다시 내리는구나. 그 비늘로 벼린 갑주를 내리노라.",
                new[]
                {
                    Objective(QuestObjectiveType.Kill, BossHyeonmu, 1, "현무를 잠재운다"),
                    Objective(QuestObjectiveType.Custom, scale, 1, "현무 비늘을 확보한다"),
                    Objective(QuestObjectiveType.Talk, NpcGojong, 1, "건청궁으로 돌아가 복명한다"),
                },
                Reward(exp: 1500, gold: 5000),
                nextQuestId: string.Empty,
                prerequisites: new[] { "mq_04_frozen_soldiers" });

            var questDatabase = CreateOrLoad<QuestDatabase>(RootFolder + "/QuestDatabase.asset");
            RegisterAssets(questDatabase, "quests", mq1, mq2, mq3, mq4, mq5);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(questDatabase);
            Debug.Log($"[SteamAttack] 메인 퀘스트 '고종의 어명 → 현무 토벌' 5장을 {RootFolder} 에 만들었다.");
        }

        // ── 생성 헬퍼 ─────────────────────────────────────────

        private static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets", "GameData");
            CreateFolderIfMissing("Assets/GameData", "NMJ");
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

        private static ObjectiveSpec Objective(QuestObjectiveType type, string targetId, int amount, string description)
        {
            return new ObjectiveSpec
            {
                Type = type,
                TargetId = targetId ?? string.Empty,
                Amount = amount,
                Description = description,
            };
        }

        private static RewardSpec Reward(int exp, int gold) => new RewardSpec { Exp = exp, Gold = gold };

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
            so.FindProperty("nextQuestId").stringValue = nextQuestId ?? string.Empty;

            var objectivesProp = so.FindProperty("objectives");
            objectivesProp.arraySize = objectives.Length;
            for (int i = 0; i < objectives.Length; i++)
            {
                var element = objectivesProp.GetArrayElementAtIndex(i);
                var spec = objectives[i];
                element.FindPropertyRelative("type").enumValueIndex = (int)spec.Type;
                element.FindPropertyRelative("targetId").stringValue = spec.TargetId;
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
        }

        private struct RewardSpec
        {
            public int Exp;
            public int Gold;
        }
    }
}
