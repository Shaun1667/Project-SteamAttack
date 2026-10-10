using System;
using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 칸(슬롯) 수로만 제한되는 인벤토리.
    ///
    /// 설계 규칙
    ///  - 하중(무게) 개념 없음. 아이템에 Weight 필드도, 총 중량 계산도 존재하지 않는다.
    ///  - 제한은 두 가지뿐: 슬롯 개수(Capacity)와 아이템별 최대 스택(ItemData.MaxStack).
    ///  - 넣을 자리가 없으면 넣지 못한 수량을 그대로 돌려준다(반환값 = 남은 개수).
    /// MonoBehaviour 가 아니므로 테스트/세이브에서 단독으로 다룰 수 있다.
    /// </summary>
    [Serializable]
    public class Inventory
    {
        public const int MinCapacity = 1;
        public const int MaxCapacity = 120;

        [SerializeField] private List<ItemStack> slots = new List<ItemStack>();

        /// <summary>슬롯 하나가 바뀔 때. 인자는 슬롯 인덱스.</summary>
        public event Action<int> SlotChanged;
        /// <summary>내용물이 어떤 식으로든 바뀔 때(정렬, 칸 확장 포함).</summary>
        public event Action Changed;
        /// <summary>아이템이 실제로 들어왔을 때(획득 로그/퀘스트 수집 판정용).</summary>
        public event Action<ItemData, int> ItemAdded;
        /// <summary>아이템이 실제로 빠져나갔을 때.</summary>
        public event Action<ItemData, int> ItemRemoved;

        public Inventory() : this(20) { }

        public Inventory(int capacity)
        {
            Resize(Mathf.Clamp(capacity, MinCapacity, MaxCapacity));
        }

        /// <summary>전체 칸 수.</summary>
        public int Capacity => slots.Count;

        /// <summary>비어 있는 칸 수.</summary>
        public int EmptySlotCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].IsEmpty) n++;
                return n;
            }
        }

        public int UsedSlotCount => Capacity - EmptySlotCount;
        public bool IsFull => EmptySlotCount == 0;

        public IReadOnlyList<ItemStack> Slots => slots;

        public ItemStack GetSlot(int index)
        {
            return IsValidIndex(index) ? slots[index] : null;
        }

        public bool IsValidIndex(int index) => index >= 0 && index < slots.Count;

        // ── 넣기 ───────────────────────────────────────────────

        /// <summary>
        /// 아이템을 넣는다. 같은 아이템 스택을 먼저 채우고, 남으면 빈 칸을 쓴다.
        /// 반환값은 칸이 모자라 <b>넣지 못한 개수</b>(0이면 전부 들어감).
        /// </summary>
        public int Add(ItemData item, int amount = 1)
        {
            if (item == null || amount <= 0) return 0;

            int remain = amount;

            // 1) 기존 스택 채우기
            if (item.IsStackable)
            {
                for (int i = 0; i < slots.Count && remain > 0; i++)
                {
                    var slot = slots[i];
                    if (!slot.CanMergeWith(item)) continue;

                    int before = remain;
                    remain = slot.Add(remain);
                    if (before != remain) RaiseSlotChanged(i);
                }
            }

            // 2) 빈 칸 사용
            for (int i = 0; i < slots.Count && remain > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;

                int put = Mathf.Min(remain, item.MaxStack);
                slots[i].Set(item, put);
                remain -= put;
                RaiseSlotChanged(i);
            }

            int added = amount - remain;
            if (added > 0)
            {
                ItemAdded?.Invoke(item, added);
                RaiseChanged();
            }

            return remain;
        }

        /// <summary>지정 슬롯에 직접 넣는다. 넣지 못한 개수를 돌려준다.</summary>
        public int AddAt(int index, ItemData item, int amount = 1)
        {
            if (!IsValidIndex(index) || item == null || amount <= 0) return amount;

            var slot = slots[index];
            int remain;

            if (slot.IsEmpty)
            {
                int put = Mathf.Min(amount, item.MaxStack);
                slot.Set(item, put);
                remain = amount - put;
            }
            else if (slot.CanMergeWith(item))
            {
                remain = slot.Add(amount);
            }
            else
            {
                return amount;
            }

            int added = amount - remain;
            if (added > 0)
            {
                RaiseSlotChanged(index);
                ItemAdded?.Invoke(item, added);
                RaiseChanged();
            }

            return remain;
        }

        /// <summary>실제로 넣지 않고, 통째로 들어갈 수 있는지만 확인한다.</summary>
        public bool CanAdd(ItemData item, int amount = 1)
        {
            return GetAcceptableAmount(item, amount) >= amount;
        }

        /// <summary>지금 상태에서 받아줄 수 있는 최대 개수.</summary>
        public int GetAcceptableAmount(ItemData item, int amount = int.MaxValue)
        {
            if (item == null || amount <= 0) return 0;

            long room = 0;
            for (int i = 0; i < slots.Count && room < amount; i++)
            {
                var slot = slots[i];
                if (slot.IsEmpty) room += item.MaxStack;
                else if (slot.CanMergeWith(item)) room += slot.FreeSpace;
            }

            return (int)Math.Min(room, amount);
        }

        // ── 빼기 ───────────────────────────────────────────────

        /// <summary>아이템을 개수만큼 소모한다. 실제로 뺀 개수를 돌려준다.</summary>
        public int Remove(ItemData item, int amount = 1)
        {
            if (item == null || amount <= 0) return 0;

            int removed = 0;

            // 뒤쪽 칸(자투리 스택)부터 정리해서 파편화를 줄인다.
            for (int i = slots.Count - 1; i >= 0 && removed < amount; i--)
            {
                var slot = slots[i];
                if (slot.IsEmpty || slot.Item != item) continue;

                removed += slot.Remove(amount - removed);
                RaiseSlotChanged(i);
            }

            if (removed > 0)
            {
                ItemRemoved?.Invoke(item, removed);
                RaiseChanged();
            }

            return removed;
        }

        /// <summary>지정 슬롯에서 덜어낸다. 실제로 뺀 개수를 돌려준다.</summary>
        public int RemoveAt(int index, int amount = 1)
        {
            if (!IsValidIndex(index) || amount <= 0) return 0;

            var slot = slots[index];
            if (slot.IsEmpty) return 0;

            var item = slot.Item;
            int removed = slot.Remove(amount);
            if (removed > 0)
            {
                RaiseSlotChanged(index);
                ItemRemoved?.Invoke(item, removed);
                RaiseChanged();
            }

            return removed;
        }

        /// <summary>해당 슬롯을 버린다. 퀘스트 아이템은 버릴 수 없다.</summary>
        public bool DiscardAt(int index)
        {
            if (!IsValidIndex(index)) return false;

            var slot = slots[index];
            if (slot.IsEmpty || !slot.Item.CanDiscard) return false;

            return RemoveAt(index, slot.Count) > 0;
        }

        public int CountOf(ItemData item)
        {
            if (item == null) return 0;

            int total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (!slot.IsEmpty && slot.Item == item) total += slot.Count;
            }

            return total;
        }

        public int CountOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;

            int total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (!slot.IsEmpty && slot.Item.ItemId == itemId) total += slot.Count;
            }

            return total;
        }

        public bool Has(ItemData item, int amount = 1) => CountOf(item) >= amount;
        public bool Has(string itemId, int amount = 1) => CountOf(itemId) >= amount;

        public int IndexOf(ItemData item)
        {
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty && slots[i].Item == item) return i;
            return -1;
        }

        public int FirstEmptySlot()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].IsEmpty) return i;
            return -1;
        }

        // ── 배치 ───────────────────────────────────────────────

        /// <summary>두 칸을 맞바꾼다. 같은 아이템이면 합칠 수 있는 만큼 합친다.</summary>
        public void Swap(int a, int b)
        {
            if (!IsValidIndex(a) || !IsValidIndex(b) || a == b) return;

            var from = slots[a];
            var to = slots[b];

            if (!from.IsEmpty && to.CanMergeWith(from.Item))
            {
                int leftover = to.Add(from.Count);
                from.Set(from.Item, leftover); // leftover 가 0이면 Set 이 알아서 칸을 비운다
            }
            else
            {
                slots[a] = to;
                slots[b] = from;
            }

            RaiseSlotChanged(a);
            RaiseSlotChanged(b);
            RaiseChanged();
        }

        /// <summary>from 칸에서 amount 만큼 떼어 to 칸으로 옮긴다(스택 나누기).</summary>
        public bool SplitTo(int from, int to, int amount)
        {
            if (!IsValidIndex(from) || !IsValidIndex(to) || from == to || amount <= 0) return false;

            var src = slots[from];
            if (src.IsEmpty || amount >= src.Count) return false;

            var dst = slots[to];
            if (dst.IsEmpty)
            {
                int put = Mathf.Min(amount, src.Item.MaxStack);
                dst.Set(src.Item, put);
                src.Remove(put);
            }
            else if (dst.CanMergeWith(src.Item))
            {
                int leftover = dst.Add(amount);
                src.Remove(amount - leftover);
            }
            else
            {
                return false;
            }

            RaiseSlotChanged(from);
            RaiseSlotChanged(to);
            RaiseChanged();
            return true;
        }

        /// <summary>같은 아이템 스택을 합치고 분류/등급/이름순으로 정렬한다.</summary>
        public void Sort()
        {
            var merged = new List<ItemStack>();

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.IsEmpty) continue;

                int remain = slot.Count;
                if (slot.Item.IsStackable)
                {
                    for (int m = 0; m < merged.Count && remain > 0; m++)
                        if (merged[m].CanMergeWith(slot.Item))
                            remain = merged[m].Add(remain);
                }

                while (remain > 0)
                {
                    int put = Mathf.Min(remain, slot.Item.MaxStack);
                    merged.Add(new ItemStack(slot.Item, put));
                    remain -= put;
                }
            }

            merged.Sort((x, y) =>
            {
                int byType = x.Item.Type.CompareTo(y.Item.Type);
                if (byType != 0) return byType;

                int byGrade = y.Item.Grade.CompareTo(x.Item.Grade);
                if (byGrade != 0) return byGrade;

                int byName = string.Compare(x.Item.DisplayName, y.Item.DisplayName, StringComparison.Ordinal);
                if (byName != 0) return byName;

                return y.Count.CompareTo(x.Count);
            });

            for (int i = 0; i < slots.Count; i++)
            {
                slots[i] = i < merged.Count ? merged[i] : new ItemStack();
                RaiseSlotChanged(i);
            }

            RaiseChanged();
        }

        // ── 칸 수 ──────────────────────────────────────────────

        /// <summary>칸을 늘린다(확장권 등). 실제로 늘어난 칸 수를 돌려준다.</summary>
        public int Expand(int extraSlots)
        {
            if (extraSlots <= 0) return 0;

            int target = Mathf.Min(Capacity + extraSlots, MaxCapacity);
            int gained = target - Capacity;
            if (gained <= 0) return 0;

            Resize(target);
            RaiseChanged();
            return gained;
        }

        /// <summary>
        /// 칸 수를 직접 지정한다. 줄일 때 잘려나가는 칸에 아이템이 있으면
        /// 남은 칸으로 옮기고, 그래도 못 넣은 아이템 목록을 돌려준다.
        /// </summary>
        public List<ItemStack> SetCapacity(int newCapacity)
        {
            var overflow = new List<ItemStack>();
            newCapacity = Mathf.Clamp(newCapacity, MinCapacity, MaxCapacity);
            if (newCapacity == Capacity) return overflow;

            if (newCapacity > Capacity)
            {
                Resize(newCapacity);
                RaiseChanged();
                return overflow;
            }

            for (int i = Capacity - 1; i >= newCapacity; i--)
            {
                var slot = slots[i];
                if (!slot.IsEmpty) overflow.Add(slot.Clone());
                slots.RemoveAt(i);
            }

            for (int i = overflow.Count - 1; i >= 0; i--)
            {
                int leftover = Add(overflow[i].Item, overflow[i].Count);
                if (leftover <= 0) overflow.RemoveAt(i);
                else overflow[i].Set(overflow[i].Item, leftover);
            }

            RaiseChanged();
            return overflow;
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty) continue;
                slots[i].Clear();
                RaiseSlotChanged(i);
            }

            RaiseChanged();
        }

        private void Resize(int capacity)
        {
            while (slots.Count < capacity) slots.Add(new ItemStack());
            while (slots.Count > capacity) slots.RemoveAt(slots.Count - 1);

            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == null) slots[i] = new ItemStack();
        }

        private void RaiseSlotChanged(int index) => SlotChanged?.Invoke(index);
        private void RaiseChanged() => Changed?.Invoke();
    }
}
