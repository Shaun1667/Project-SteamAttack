using System.Collections.Generic;
using SteamAttack.InventorySystem;
using SteamAttack.Items;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SteamAttack.UI
{
    /// <summary>
    /// 인벤토리 창. 칸 수만큼 슬롯을 만들고, 칸 사용량(12/20)을 보여준다.
    /// 무게 게이지는 없다 — 이 게임의 인벤토리는 칸 수로만 제한된다.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform slotParent;
        [SerializeField] private InventorySlotUI slotPrefab;

        [Header("정보 표시")]
        [SerializeField] private TMP_Text capacityText;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text itemDescriptionText;

        [Header("드래그")]
        [SerializeField] private RectTransform dragIcon;
        [SerializeField] private Image dragIconImage;

        [Header("조작")]
        [SerializeField] private Key toggleKey = Key.I;
        [SerializeField] private Button sortButton;

        private readonly List<InventorySlotUI> slots = new List<InventorySlotUI>();
        private Inventory inventory;
        private InventorySlotUI draggingSlot;
        private int selectedIndex = -1;

        private void Awake()
        {
            if (playerInventory == null) playerInventory = PlayerInventory.Instance;
            if (sortButton != null) sortButton.onClick.AddListener(SortInventory);
            if (dragIcon != null) dragIcon.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (playerInventory == null) playerInventory = PlayerInventory.Instance;
            if (playerInventory == null)
            {
                Debug.LogWarning("[InventoryUI] PlayerInventory 를 찾지 못했다.");
                return;
            }

            inventory = playerInventory.Inventory;
            inventory.SlotChanged += RefreshSlot;
            inventory.Changed += RefreshAll;

            BuildSlots();
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (inventory == null) return;

            inventory.SlotChanged -= RefreshSlot;
            inventory.Changed -= RefreshAll;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[toggleKey].wasPressedThisFrame) Toggle();
        }

        public bool IsOpen => panel != null && panel.activeSelf;

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (panel != null) panel.SetActive(open);
            if (open) RefreshAll();
        }

        // ── 슬롯 구성 ──────────────────────────────────────────

        private void BuildSlots()
        {
            if (slotParent == null || slotPrefab == null)
            {
                Debug.LogWarning("[InventoryUI] slotParent / slotPrefab 을 지정해라.");
                return;
            }

            for (int i = slots.Count - 1; i >= 0; i--)
            {
                if (slots[i] == null) continue;

                // Destroy 는 프레임 끝에 처리되므로, 레이아웃에서 먼저 떼어낸다.
                slots[i].transform.SetParent(null, false);
                Destroy(slots[i].gameObject);
            }

            slots.Clear();

            for (int i = 0; i < inventory.Capacity; i++)
            {
                var slot = Instantiate(slotPrefab, slotParent);
                slot.name = $"Slot_{i:00}";
                slot.Bind(this, i);
                slots.Add(slot);
            }
        }

        public void RefreshAll()
        {
            if (inventory == null) return;

            // 칸 확장 보상 등으로 칸 수가 늘어났다면 슬롯을 다시 만든다.
            if (slots.Count != inventory.Capacity) BuildSlots();

            for (int i = 0; i < slots.Count; i++)
                slots[i].Refresh(inventory.GetSlot(i));

            if (capacityText != null)
                capacityText.text = $"{inventory.UsedSlotCount} / {inventory.Capacity} 칸";
        }

        private void RefreshSlot(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            slots[index].Refresh(inventory.GetSlot(index));

            if (capacityText != null)
                capacityText.text = $"{inventory.UsedSlotCount} / {inventory.Capacity} 칸";
        }

        // ── 상호작용 ───────────────────────────────────────────

        public void SelectSlot(int index)
        {
            selectedIndex = index;

            var stack = inventory != null ? inventory.GetSlot(index) : null;
            bool hasItem = stack != null && !stack.IsEmpty;

            if (itemNameText != null)
                itemNameText.text = hasItem ? stack.Item.DisplayName : string.Empty;

            if (itemDescriptionText != null)
                itemDescriptionText.text = hasItem ? stack.Item.Description : string.Empty;
        }

        /// <summary>우클릭 사용. 소비 아이템은 1개 소모한다(효과 처리는 게임 쪽에서 연결).</summary>
        public void UseSlot(int index)
        {
            var stack = inventory != null ? inventory.GetSlot(index) : null;
            if (stack == null || stack.IsEmpty) return;

            if (stack.Item.Type == ItemType.Consumable) inventory.RemoveAt(index, 1);
        }

        /// <summary>선택한 칸을 버린다. 퀘스트 아이템은 버려지지 않는다.</summary>
        public void DiscardSelected()
        {
            if (inventory == null || selectedIndex < 0) return;
            inventory.DiscardAt(selectedIndex);
        }

        public void SortInventory() => inventory?.Sort();

        // ── 드래그 ─────────────────────────────────────────────

        internal void BeginDrag(InventorySlotUI slot)
        {
            draggingSlot = slot;

            var stack = inventory.GetSlot(slot.Index);
            if (stack == null || stack.IsEmpty || dragIcon == null) return;

            dragIcon.gameObject.SetActive(true);
            if (dragIconImage != null) dragIconImage.sprite = stack.Item.Icon;
        }

        internal void UpdateDrag(Vector2 screenPosition)
        {
            if (dragIcon != null && dragIcon.gameObject.activeSelf) dragIcon.position = screenPosition;
        }

        internal void EndDrag()
        {
            draggingSlot = null;
            if (dragIcon != null) dragIcon.gameObject.SetActive(false);
        }

        internal void DropOn(InventorySlotUI target)
        {
            if (draggingSlot == null || target == null || inventory == null) return;
            if (draggingSlot.Index == target.Index) return;

            inventory.Swap(draggingSlot.Index, target.Index);
        }
    }
}
