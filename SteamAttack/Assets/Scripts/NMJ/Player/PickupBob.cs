using UnityEngine;

namespace NMJ
{
    /// <summary>필드에 떨어진 아이템이 눈에 띄도록 천천히 돌고 떠다니게 한다.</summary>
    public class PickupBob : MonoBehaviour
    {
        [SerializeField] private float spinDegreesPerSecond = 60f;
        [SerializeField] private float bobHeight = 0.25f;
        [SerializeField] private float bobSpeed = 2f;

        private Vector3 basePosition;
        private float phase;

        private void Start()
        {
            basePosition = transform.position;
            phase = Random.value * Mathf.PI * 2f; // 여러 개가 같이 움직이지 않게 흩어 놓는다
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
            transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight);
        }
    }
}
