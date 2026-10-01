using SteamAttack.Items;
using UnityEngine;

// 인스펙터에서만 채우는 직렬화 필드라 "할당된 적 없음" 경고는 의미가 없다.
#pragma warning disable CS0649

namespace SteamAttack.InventorySystem
{
    /// <summary>
    /// 플레이어가 들고 다니는 인벤토리. 씬에 하나 두고 <see cref="Instance"/> 로 접근한다.
    /// 하중 제한은 없고, 인스펙터의 <c>slotCount</c> 만큼만 칸이 생긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Instance { get; private set; }

        [Header("칸 수 (하중 제한 없음)")]
        [SerializeField, Range(Inventory.MinCapacity, Inventory.MaxCapacity)]
        private int slotCount = 20;

        [Header("시작 지급 아이템")]
        [SerializeField] private StartingItem[] startingItems;

        private Inventory inventory;

        public Inventory Inventory
        {
            get
            {
                if (inventory == null) inventory = new Inventory(slotCount);
                return inventory;
            }
        }

        public int Capacity => Inventory.Capacity;
        public int EmptySlotCount => Inventory.EmptySlotCount;
        public bool IsFull => Inventory.IsFull;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            inventory = new Inventory(slotCount);
        }

        private void Start()
        {
            if (startingItems == null) return;

            for (int i = 0; i < startingItems.Length; i++)
            {
                var entry = startingItems[i];
                if (entry.item == null) continue;
                Inventory.Add(entry.item, Mathf.Max(1, entry.count));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>전부 넣을 수 있을 때만 넣는다. 칸이 모자라면 아무것도 넣지 않고 false.</summary>
        public bool TryAddAll(ItemData item, int amount = 1)
        {
            if (!Inventory.CanAdd(item, amount)) return false;

            Inventory.Add(item, amount);
            return true;
        }

        /// <summary>넣을 수 있는 만큼만 넣고, 칸이 없어 흘린 개수를 돌려준다.</summary>
        public int AddPartial(ItemData item, int amount = 1) => Inventory.Add(item, amount);

        public int Remove(ItemData item, int amount = 1) => Inventory.Remove(item, amount);
        public bool Has(ItemData item, int amount = 1) => Inventory.Has(item, amount);
        public bool Has(string itemId, int amount = 1) => Inventory.Has(itemId, amount);
        public int CountOf(string itemId) => Inventory.CountOf(itemId);

        /// <summary>인벤토리 칸 확장. 늘어난 칸 수를 돌려준다.</summary>
        public int ExpandSlots(int extraSlots)
        {
            int gained = Inventory.Expand(extraSlots);
            if (gained > 0) slotCount = Inventory.Capacity;
            return gained;
        }

        private void OnValidate()
        {
            slotCount = Mathf.Clamp(slotCount, Inventory.MinCapacity, Inventory.MaxCapacity);

            if (Application.isPlaying && inventory != null && inventory.Capacity != slotCount)
                inventory.SetCapacity(slotCount);
        }

        [System.Serializable]
        private struct StartingItem
        {
            public ItemData item;
            [Min(1)] public int count;
        }
    }
}
