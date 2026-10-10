using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 아이템 원본 데이터(ScriptableObject).
    /// 무게(하중) 개념은 사용하지 않는다. 인벤토리 제약은 오직 칸 수와 스택 수량뿐이다.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "SteamAttack/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        [Header("식별")]
        [SerializeField] private string itemId = "item_new";
        [SerializeField] private string displayName = "새 아이템";
        [SerializeField, TextArea(2, 5)] private string description = "";

        [Header("분류")]
        [SerializeField] private ItemType itemType = ItemType.Etc;
        [SerializeField] private ItemGrade grade = ItemGrade.Common;

        [Header("스택")]
        [SerializeField, Min(1)] private int maxStack = 99;

        [Header("표시")]
        [SerializeField] private Sprite icon;

        /// <summary>저장/퀘스트 판정에 사용하는 고유 키.</summary>
        public string ItemId => string.IsNullOrEmpty(itemId) ? name : itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public ItemType Type => itemType;
        public ItemGrade Grade => grade;

        /// <summary>한 칸에 쌓을 수 있는 최대 개수. 1이면 스택 불가 아이템.</summary>
        public int MaxStack => Mathf.Max(1, maxStack);
        public bool IsStackable => MaxStack > 1;
        public Sprite Icon => icon;

        /// <summary>퀘스트 아이템은 임의로 버릴 수 없게 막는다.</summary>
        public bool CanDiscard => itemType != ItemType.Quest;

        public Color GradeColor
        {
            get
            {
                switch (grade)
                {
                    case ItemGrade.Uncommon: return new Color(0.44f, 0.80f, 0.40f);
                    case ItemGrade.Rare: return new Color(0.35f, 0.62f, 0.95f);
                    case ItemGrade.Unique: return new Color(0.78f, 0.46f, 0.94f);
                    case ItemGrade.Legendary: return new Color(0.98f, 0.71f, 0.25f);
                    default: return new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(itemId)) itemId = name;
            if (maxStack < 1) maxStack = 1;
        }
#endif
    }
}
