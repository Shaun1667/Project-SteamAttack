using System.IO;
using UnityEditor;
using UnityEngine;

namespace NMJ.EditorTools
{
    /// <summary>
    /// 5분 시연용 퀘스트 한 줄기 (기획서 v4 4-2, 세계관 v6 5장).
    /// 오프닝에서 어명을 이미 받았으므로 육조거리부터 시작한다.
    ///
    ///   dq_01 궁 안을 지나          근정전 마당의 적을 잠재운다 (0/4) · 광화문 안쪽에 닿는다
    ///   dq_02 한 호흡에, 한 점에     광화문을 지키는 적을 잠재운다 (0/4) · 얼음 문을 깬다
    ///   dq_03 현무를 잠재우라        육조거리 안개문으로 들어간다 · 현무를 잠재운다
    ///
    /// 목표 아이디 (씬 쪽에서 보고):
    ///   enemy_yukjo / enemy_gwanghwa — NMJ_EnemySpawns의 적에 붙은 QuestKillReporter
    ///   loc_gwanghwamun / loc_hyangwonjeong — QuestZoneTrigger
    ///   gate_gwanghwamun — IceGate가 깨질 때 Custom
    ///   boss_hyeonmu — BossDefeatBanner가 뜰 때
    ///
    /// 메뉴: SteamAttack ▸ NMJ ▸ 시연 퀘스트 만들기 (같은 자리에 덮어써 guid 유지)
    /// </summary>
    public static class NmjDemoQuestBuilder
    {
        public const string Folder = "Assets/GameData/NMJ/DemoQuests";
        public const string DatabasePath = Folder + "/NMJ_DemoQuestDatabase.asset";

        private struct Obj
        {
            public QuestObjectiveType type;
            public string id;
            public int amount;
            public string text;
        }

        [MenuItem("SteamAttack/NMJ/시연 퀘스트 만들기")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameData/NMJ", "DemoQuests");

            var q1 = Quest("dq_01_yukjo", "궁 안을 지나",
                "멈춘 백성 사이를 지나 광화문으로 간다. 베인 이는 죽지 않고 얼음이 깨지며 잠든다.",
                new[]
                {
                    new Obj { type = QuestObjectiveType.Kill, id = "enemy_yukjo", amount = 4, text = "근정전 마당의 적을 잠재운다" },
                    new Obj { type = QuestObjectiveType.Reach, id = "loc_gwanghwamun", amount = 1, text = "광화문 안쪽에 닿는다" },
                }, "dq_02_gwanghwamun", null);

            var q2 = Quest("dq_02_gwanghwamun", "한 호흡에, 한 점에",
                "얼음으로 봉한 광화문을 연다. 내려치는 창끝의 한 점을 받아치고, 숨을 고르는 사이 강공격을 넣는다.",
                new[]
                {
                    new Obj { type = QuestObjectiveType.Kill, id = "enemy_gwanghwa", amount = 4, text = "광화문을 지키는 적을 잠재운다" },
                    new Obj { type = QuestObjectiveType.Custom, id = "gate_gwanghwamun", amount = 1, text = "얼음 문을 깬다" },
                }, "dq_03_hyeonmu", "dq_01_yukjo");

            var q3 = Quest("dq_03_hyeonmu", "현무를 잠재우라",
                "향원정 안개문 너머, 얼어붙은 연못 위의 현무. 다리를 무너뜨리고 신핵을 쳐 삼킨 열을 토하게 한다.",
                new[]
                {
                    new Obj { type = QuestObjectiveType.Reach, id = "loc_hyangwonjeong", amount = 1, text = "육조거리 안개문으로 들어간다" },
                    new Obj { type = QuestObjectiveType.Kill, id = "boss_hyeonmu", amount = 1, text = "현무를 잠재운다" },
                }, "", "dq_02_gwanghwamun");

            var db = Load<QuestDatabase>(DatabasePath);
            var so = new SerializedObject(db);
            var list = so.FindProperty("quests");
            list.arraySize = 3;
            list.GetArrayElementAtIndex(0).objectReferenceValue = q1;
            list.GetArrayElementAtIndex(1).objectReferenceValue = q2;
            list.GetArrayElementAtIndex(2).objectReferenceValue = q3;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log("[NMJ] 시연 퀘스트 3개와 데이터베이스를 만들었다: " + DatabasePath);
        }

        private static T Load<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static QuestData Quest(string id, string title, string summary, Obj[] objectives, string next, string prerequisite)
        {
            var q = Load<QuestData>($"{Folder}/Quest_{id}.asset");
            var so = new SerializedObject(q);
            so.FindProperty("questId").stringValue = id;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("category").enumValueIndex = (int)QuestCategory.Main;
            so.FindProperty("summary").stringValue = summary;
            so.FindProperty("questGiverId").stringValue = "";
            so.FindProperty("startDialogue").stringValue = "";
            so.FindProperty("completeDialogue").stringValue = "";
            so.FindProperty("autoComplete").boolValue = true;   // 다 채우면 보고 없이 다음 단계로
            so.FindProperty("nextQuestId").stringValue = next;

            var objs = so.FindProperty("objectives");
            objs.arraySize = objectives.Length;
            for (int i = 0; i < objectives.Length; i++)
            {
                var e = objs.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("type").enumValueIndex = (int)objectives[i].type;
                e.FindPropertyRelative("targetId").stringValue = objectives[i].id;
                e.FindPropertyRelative("targetItem").objectReferenceValue = null;
                e.FindPropertyRelative("description").stringValue = objectives[i].text;
                e.FindPropertyRelative("requiredAmount").intValue = objectives[i].amount;
            }

            var pre = so.FindProperty("prerequisiteQuestIds");
            pre.arraySize = string.IsNullOrEmpty(prerequisite) ? 0 : 1;
            if (pre.arraySize == 1) pre.GetArrayElementAtIndex(0).stringValue = prerequisite;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(q);
            return q;
        }
    }
}
