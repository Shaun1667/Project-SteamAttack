using UnityEngine;
using UnityEngine.InputSystem;

namespace SteamAttack.PlayerControl
{
    /// <summary>
    /// 테스트용 플레이어 이동. CharacterController 로 움직이므로
    /// 트리거로 된 <c>ItemPickup</c> 위를 지나가면 OnTriggerEnter 가 발생한다.
    ///
    /// 조작: WASD 이동 / Shift 달리기 / Space 점프 / 마우스 오른쪽 드래그로 시점 회전
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMover : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField] private float walkSpeed = 6f;
        [SerializeField] private float sprintSpeed = 14f;
        [SerializeField] private float turnSpeed = 720f;

        [Header("중력")]
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float jumpSpeed = 8f;

        [Tooltip("이동 방향의 기준이 되는 카메라. 비우면 Camera.main 을 쓴다.")]
        [SerializeField] private Transform viewTransform;

        private CharacterController controller;
        private float verticalVelocity;

        public bool IsMoving { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (viewTransform == null && Camera.main != null) viewTransform = Camera.main.transform;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = ReadMoveInput(keyboard);
            Vector3 move = ToWorldDirection(input);
            IsMoving = move.sqrMagnitude > 0.0001f;

            float speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;

            if (controller.isGrounded)
            {
                verticalVelocity = -2f; // 경사면에서 붕 뜨지 않게 살짝 눌러준다
                if (keyboard.spaceKey.wasPressedThisFrame) verticalVelocity = jumpSpeed;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 velocity = move * speed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            if (!IsMoving) return;

            var look = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput(Keyboard keyboard)
        {
            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        /// <summary>카메라가 보는 방향 기준으로 입력을 월드 방향으로 바꾼다.</summary>
        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (viewTransform != null)
            {
                forward = Vector3.ProjectOnPlane(viewTransform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
                right = Vector3.Cross(Vector3.up, forward);
            }

            Vector3 move = forward * input.y + right * input.x;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }
    }
}
