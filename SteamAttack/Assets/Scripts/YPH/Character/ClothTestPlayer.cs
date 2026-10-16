using UnityEngine;
using UnityEngine.InputSystem;

namespace YPH
{
    /// <summary>옷 시험용 키보드 이동과 대기·걷기·달리기 전환입니다. 옷 물리는 각 Cloth가 담당합니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ClothTestPlayer : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField, Tooltip("Visual의 Humanoid Animator입니다. Speed와 AnimSpeed 파라미터가 필요하며, 비우면 시험 이동을 중단합니다.")]
        private Animator _animator;
        [SerializeField, Tooltip("이동 방향의 기준 카메라입니다. 비우면 시작할 때 MainCamera를 사용하고, 카메라도 없으면 월드 앞쪽이 W 방향입니다.")]
        private Transform _view;
        [SerializeField, Tooltip("애니메이션 재생 중 Visual의 높이(m)입니다. FBX의 편집 원점과 Humanoid의 발 기준 차이를 보정하며, 높이면 몸 전체가 올라갑니다.")]
        private float _animatedVisualHeight = 0.02f;

        [Header("이동 · 단위 m/s")]
        [SerializeField, Min(0f), Tooltip("걷기 속도(m/s)입니다. 높이면 빠르게 이동합니다. Mixamo 걷기의 보폭에 맞춘 기본값은 0.75입니다.")]
        private float _walkSpeed = 0.75f;
        [SerializeField, Min(0f), Tooltip("달리기 속도(m/s)입니다. 높이면 옷에 더 큰 이동 변화가 생깁니다. 기본값은 3입니다.")]
        private float _runSpeed = 3f;
        [SerializeField, Min(0f), Tooltip("이동 방향으로 돌아서는 속도(도/초)입니다. 높이면 급하게 회전하며 옷의 관성이 커집니다.")]
        private float _turnSpeed = 360f;
        [SerializeField, Tooltip("아래로 당기는 가속도(m/s²)입니다. 음수여야 하며, Cloth 자체의 중력 설정과는 별개입니다.")]
        private float _gravity = -20f;
        [SerializeField, Min(0.01f), Tooltip("모든 이동 애니메이션의 재생 배율입니다. 1은 원래 속도이며 실제 이동 속도는 바뀌지 않습니다.")]
        private float _animationSpeed = 1f;
        [SerializeField, Min(0f), Tooltip("대기·걷기·달리기를 섞고 이동 속도를 따라 바꾸는 시간(초)입니다. 높이면 출발·정지가 부드러워지며, 0이면 즉시 바뀝니다.")]
        private float _blendTime = 0.12f;

        [Header("시험 키 · Editor / Development Build")]
        [SerializeField, Tooltip("카메라가 보는 앞으로 걷는 키입니다. None이면 이 방향 입력을 사용하지 않습니다.")]
        private Key _forward = Key.W;
        [SerializeField, Tooltip("카메라 기준 뒤로 걷는 키입니다. 캐릭터는 이동 방향으로 돌아섭니다.")]
        private Key _backward = Key.S;
        [SerializeField, Tooltip("카메라 기준 왼쪽으로 걷는 키입니다. None이면 사용하지 않습니다.")]
        private Key _left = Key.A;
        [SerializeField, Tooltip("카메라 기준 오른쪽으로 걷는 키입니다. None이면 사용하지 않습니다.")]
        private Key _right = Key.D;
        [SerializeField, Tooltip("이동 키와 함께 누르면 달리는 키입니다. 기본값은 왼쪽 Shift입니다.")]
        private Key _run = Key.LeftShift;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AnimationSpeedId = Animator.StringToHash("AnimSpeed");
        private const float GroundVelocity = -2f;
        private CharacterController _controller;
        private float _verticalVelocity;

        private void Awake()
        {
#if !UNITY_EDITOR
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }
#endif
            _controller = GetComponent<CharacterController>();
            if (_animator == null || _animator.avatar == null || !_animator.isHuman || _animator.runtimeAnimatorController == null)
            {
                Debug.LogError("ClothTestPlayer: Humanoid Avatar와 시험용 Animator Controller를 연결하세요.", this);
                enabled = false;
                return;
            }
            // 편집 시 T 자세와 Humanoid 재생 시의 발 기준이 서로 다릅니다.
            Vector3 visualPosition = _animator.transform.localPosition;
            visualPosition.y = _animatedVisualHeight;
            _animator.transform.localPosition = visualPosition;
        }

        private void Start()
        {
            if (_view == null && Camera.main != null)
            {
                _view = Camera.main.transform;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Vector2 input = Vector2.zero;
            bool running = false;
            if (keyboard != null)
            {
                input.x = (Pressed(keyboard, _right) ? 1f : 0f) - (Pressed(keyboard, _left) ? 1f : 0f);
                input.y = (Pressed(keyboard, _forward) ? 1f : 0f) - (Pressed(keyboard, _backward) ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
                running = Pressed(keyboard, _run);
            }

            Vector3 forward = _view != null ? Vector3.ProjectOnPlane(_view.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            Vector3 direction = forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x;
            _animator.SetFloat(SpeedId, input.magnitude * (running ? 1f : 0.5f), _blendTime, Time.deltaTime);
            float movementBlend = _animator.GetFloat(SpeedId);
            if (input.sqrMagnitude == 0f && movementBlend < 0.001f)
            {
                movementBlend = 0f;
                _animator.SetFloat(SpeedId, 0f);
            }
            // 보폭이 바뀌는 비율과 실제 속도를 맞춰 출발·정지 때 미끄러짐을 줄입니다.
            float movementSpeed = movementBlend <= 0.5f
                ? Mathf.Lerp(0f, _walkSpeed, movementBlend * 2f)
                : Mathf.Lerp(_walkSpeed, _runSpeed, (movementBlend - 0.5f) * 2f);
            if (direction.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), _turnSpeed * Time.deltaTime);
            }
            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = GroundVelocity;
            }
            _verticalVelocity += _gravity * Time.deltaTime;
            _controller.Move((transform.forward * movementSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
            _animator.SetFloat(AnimationSpeedId, _animationSpeed);
        }

        private static bool Pressed(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key].isPressed;
        }

        private void OnDisable()
        {
            _verticalVelocity = 0f;
            if (_animator != null && _animator.isInitialized)
            {
                _animator.SetFloat(SpeedId, 0f);
            }
        }
    }
}
