using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>questId -> QuestData 조회 테이블. QuestManager 가 이걸 들고 시작한다.</summary>
    [CreateAssetMenu(fileName = "QuestDatabase", menuName = "SteamAttack/Quest Database", order = 11)]
    public class QuestDatabase : ScriptableObject
    {
        [SerializeField] private List<QuestData> quests = new List<QuestData>();

        private Dictionary<string, QuestData> lookup;

        public IReadOnlyList<QuestData> Quests => quests;

        public QuestData Find(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            BuildLookup();
            return lookup.TryGetValue(questId, out var data) ? data : null;
        }

        public void Register(QuestData data)
        {
            if (data == null || quests.Contains(data)) return;
            quests.Add(data);
            lookup = null;
        }

        private void BuildLookup()
        {
            if (lookup != null) return;

            lookup = new Dictionary<string, QuestData>(quests.Count);
            for (int i = 0; i < quests.Count; i++)
            {
                var data = quests[i];
                if (data == null) continue;
                lookup[data.QuestId] = data;
            }
        }

        private void OnEnable() => lookup = null;
        private void OnValidate() => lookup = null;
    }
}
