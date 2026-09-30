using SteamAttack.Items;
using UnityEngine;
using UnityEngine.Events;

namespace SteamAttack.InventorySystem
{
    /// <summary>
    /// 필드에 떨어져 있는 아이템. 플레이어가 닿으면 인벤토리에 들어간다.
    /// 칸이 모자라면 줍지 못하고 남은 수량만큼 그대로 필드에 남는다(하중 제한은 없다).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [SerializeField] private ItemData item;
        [SerializeField, Min(1)] private int count = 1;
        [SerializeField] private string playerTag = "Player";

        [Tooltip("칸이 부족하면 아예 줍지 않는다. 끄면 들어가는 만큼만 줍는다.")]
        [SerializeField] private bool requireFullSpace;

        public UnityEvent<ItemData, int> OnPickedUp;
        public UnityEvent OnInventoryFull;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (item == null || !other.CompareTag(playerTag)) return;

            var player = PlayerInventory.Instance;
            if (player == null) return;

            if (requireFullSpace)
            {
                if (!player.TryAddAll(item, count))
                {
                    OnInventoryFull?.Invoke();
                    return;
                }

                OnPickedUp?.Invoke(item, count);
                Destroy(gameObject);
                return;
            }

            int leftover = player.AddPartial(item, count);
            int picked = count - leftover;

            if (picked > 0) OnPickedUp?.Invoke(item, picked);

            if (leftover > 0)
            {
                count = leftover;
                OnInventoryFull?.Invoke();
                return;
            }

            Destroy(gameObject);
        }
    }
}
