using System;
using UnityEngine;

namespace NMJ
{
    /// <summary>인벤토리 한 칸의 내용물. 비어 있으면 Item 이 null 이다.</summary>
    [Serializable]
    public class ItemStack
    {
        [SerializeField] private ItemData item;
        [SerializeField] private int count;

        public ItemStack() { }

        public ItemStack(ItemData item, int count)
        {
            Set(item, count);
        }

        public ItemData Item => item;
        public int Count => count;
        public bool IsEmpty => item == null || count <= 0;

        /// <summary>이 칸에 더 넣을 수 있는 여유 개수.</summary>
        public int FreeSpace => IsEmpty ? 0 : Mathf.Max(0, item.MaxStack - count);

        public void Set(ItemData newItem, int newCount)
        {
            if (newItem == null || newCount <= 0)
            {
                Clear();
                return;
            }

            item = newItem;
            count = Mathf.Clamp(newCount, 1, newItem.MaxStack);
        }

        public void Clear()
        {
            item = null;
            count = 0;
        }

        public bool CanMergeWith(ItemData other)
        {
            return !IsEmpty && other != null && item == other && item.IsStackable && FreeSpace > 0;
        }

        /// <summary>amount 만큼 채우고, 넘쳐서 못 넣은 개수를 돌려준다.</summary>
        public int Add(int amount)
        {
            if (IsEmpty || amount <= 0) return amount;

            int accepted = Mathf.Min(amount, FreeSpace);
            count += accepted;
            return amount - accepted;
        }

        /// <summary>amount 만큼 덜어내고, 실제로 덜어낸 개수를 돌려준다.</summary>
        public int Remove(int amount)
        {
            if (IsEmpty || amount <= 0) return 0;

            int removed = Mathf.Min(amount, count);
            count -= removed;
            if (count <= 0) Clear();
            return removed;
        }

        public ItemStack Clone()
        {
            return IsEmpty ? new ItemStack() : new ItemStack(item, count);
        }

        public override string ToString()
        {
            return IsEmpty ? "(빈 칸)" : $"{item.DisplayName} x{count}";
        }
    }
}
