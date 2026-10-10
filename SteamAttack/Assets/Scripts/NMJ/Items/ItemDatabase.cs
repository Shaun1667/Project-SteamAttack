using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// itemId -> ItemData 조회용 테이블.
    /// 퀘스트 보상 지급, 세이브 복원처럼 문자열 키로 아이템을 찾아야 할 때 쓴다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "SteamAttack/Item Database", order = 1)]
    public class ItemDatabase : ScriptableObject
    {
        [SerializeField] private List<ItemData> items = new List<ItemData>();

        private Dictionary<string, ItemData> lookup;

        public IReadOnlyList<ItemData> Items => items;

        public ItemData Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            BuildLookup();
            return lookup.TryGetValue(itemId, out var data) ? data : null;
        }

        public void Register(ItemData data)
        {
            if (data == null || items.Contains(data)) return;
            items.Add(data);
            lookup = null;
        }

        private void BuildLookup()
        {
            if (lookup != null) return;

            lookup = new Dictionary<string, ItemData>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var data = items[i];
                if (data == null) continue;
                lookup[data.ItemId] = data;
            }
        }

        private void OnEnable() => lookup = null;
        private void OnValidate() => lookup = null;
    }
}
