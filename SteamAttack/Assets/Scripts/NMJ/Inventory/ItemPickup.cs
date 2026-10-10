using UnityEngine;
using UnityEngine.Events;

namespace NMJ
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

        private const float RetryInterval = 0.5f;
        private float nextRetryTime;

        /// <summary>
        /// 한 번 주워진 뒤의 중복 지급을 막는다.
        /// <c>Destroy</c> 는 프레임 끝에야 처리되므로, 그 전에 들어오는 트리거 콜백이
        /// 수량을 한 번 더 지급하는 것을 이 래치로 끊는다.
        /// </summary>
        private bool isPickedUp;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnEnable()
        {
            // 진입한 바로 그 프레임에 OnTriggerStay 재시도가 열리지 않게 한 박자 늦춘다.
            nextRetryTime = Time.time + RetryInterval;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryPickup(other);
        }

        /// <summary>
        /// 칸이 꽉 차서 못 주운 아이템을 밟고 선 채로 가방을 비우면 바로 주워지도록,
        /// 머물러 있는 동안에도 주기적으로 다시 시도한다.
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            if (isPickedUp || Time.time < nextRetryTime) return;

            nextRetryTime = Time.time + RetryInterval;
            TryPickup(other);
        }

        private void TryPickup(Collider other)
        {
            if (isPickedUp || item == null || !other.CompareTag(playerTag)) return;

            var player = PlayerInventory.Instance;
            if (player == null) return;

            if (requireFullSpace)
            {
                if (!player.TryAddAll(item, count))
                {
                    OnInventoryFull?.Invoke();
                    return;
                }

                isPickedUp = true;
                OnPickedUp?.Invoke(item, count);
                Destroy(gameObject);
                return;
            }

            int leftover = player.AddPartial(item, count);
            int picked = count - leftover;

            // 전부 주웠으면 이벤트를 쏘기 전에 잠가서, 같은 프레임의 다른 트리거 콜백이
            // (그리고 이벤트 핸들러가) 다시 들어와도 수량이 또 지급되지 않게 한다.
            if (leftover <= 0) isPickedUp = true;

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
