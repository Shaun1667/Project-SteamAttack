using UnityEngine;
using UnityEngine.InputSystem;

namespace SteamAttack.PlayerControl
{
    /// <summary>
    /// 플레이어를 뒤에서 따라다니는 카메라. 씬에 원래 있던 카메라에 붙여 쓴다.
    /// 재생 중에만 위치를 바꾸므로 씬에 저장된 카메라 구도는 그대로 남는다.
    /// 마우스 오른쪽 버튼을 누른 채 좌우로 끌면 시점이 돈다.
    /// </summary>
    public class PlayerFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -8f);
        [SerializeField] private float lookHeight = 1.4f;
        [SerializeField] private float followSharpness = 10f;

        [Header("시점 회전 (마우스 오른쪽 드래그)")]
        [SerializeField] private float yaw;
        [SerializeField] private float mouseSensitivity = 0.15f;

        private Vector3 savedPosition;
        private Quaternion savedRotation;

        public void SetTarget(Transform newTarget) => target = newTarget;

        private void Awake()
        {
            savedPosition = transform.position;
            savedRotation = transform.rotation;
        }

        private void OnDisable()
        {
            // 재생을 멈췄을 때 원래 구도로 돌려놓는다.
            transform.SetPositionAndRotation(savedPosition, savedRotation);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
                yaw += mouse.delta.ReadValue().x * mouseSensitivity;

            Quaternion spin = Quaternion.Euler(0f, yaw, 0f);
            Vector3 desired = target.position + spin * offset;

            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(target.position + Vector3.up * lookHeight);
        }
    }
}
