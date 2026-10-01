using SteamAttack.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteamAttack.UI
{
    /// <summary>인벤토리 칸 한 개의 UI. 드래그로 자리 이동/스택 합치기를 지원한다.</summary>
    public class InventorySlotUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Image gradeFrame;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private GameObject emptyOverlay;

        private InventoryUI owner;
        private int index = -1;

        public int Index => index;
        public bool IsEmpty { get; private set; } = true;

        public void Bind(InventoryUI inventoryUI, int slotIndex)
        {
            owner = inventoryUI;
            index = slotIndex;
        }

        public void Refresh(ItemStack stack)
        {
            IsEmpty = stack == null || stack.IsEmpty;

            if (iconImage != null)
            {
                iconImage.enabled = !IsEmpty;
                iconImage.sprite = IsEmpty ? null : stack.Item.Icon;

                // 아이콘 스프라이트가 아직 없으면 등급 색 사각형을 임시로 보여준다.
                if (!IsEmpty)
                    iconImage.color = stack.Item.Icon != null ? Color.white : stack.Item.GradeColor;
            }

            if (gradeFrame != null)
            {
                gradeFrame.enabled = !IsEmpty;
                if (!IsEmpty) gradeFrame.color = stack.Item.GradeColor;
            }

            if (countText != null)
            {
                bool showCount = !IsEmpty && stack.Count > 1;
                countText.enabled = showCount;
                if (showCount) countText.text = stack.Count.ToString();
            }

            if (emptyOverlay != null) emptyOverlay.SetActive(IsEmpty);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (IsEmpty || owner == null) return;
            owner.BeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.UpdateDrag(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.EndDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.DropOn(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner == null || IsEmpty) return;

            if (eventData.button == PointerEventData.InputButton.Right) owner.UseSlot(index);
            else owner.SelectSlot(index);
        }
    }
}
