using UnityEngine;

namespace YPH
{
    /// <summary>조준 중 시야각을 좁히고 머리가 화면 왼쪽 아래에 오도록 카메라를 옮깁니다.</summary>
    // 기본 카메라 이동과 플레이어 조준 회전이 끝난 뒤 최종 구도를 맞춥니다.
    [DefaultExecutionOrder(150)]
    public class AimZoomView : MonoBehaviour
    {
        [SerializeField, Tooltip("조준 비율을 알려 줄 컨트롤러입니다. 없으면 동작을 멈춥니다.")]
        private AimController _controller;
        [SerializeField, Tooltip("줌을 적용할 카메라입니다. 조준 시작 당시의 시야각으로 복구합니다.")]
        private Camera _camera;
        [SerializeField, Range(0.3f, 1f), Tooltip("조준 시 원래 시야각에 곱할 배율입니다. 작을수록 확대되며 1이면 줌이 없습니다. 50도에서 0.8이면 40도입니다.")]
        private float _aimFovMultiplier = 0.8f;
        [SerializeField, Tooltip("조준 중 거리를 줄일 ThirdPersonCamera입니다. 비우면 기존 거리와 줌 동작을 유지합니다.")]
        private NGH.ThirdPersonCamera _orbitCamera;
        [SerializeField, Min(0.2f), Tooltip("조준 중 카메라 거리(m)입니다. 낮추면 플레이어에 가까워지고 크게 보입니다. 해제하면 조준 전 거리로 복구하며 벽 충돌은 기존 카메라가 처리합니다.")]
        private float _aimDistance = 0.92f;
        [SerializeField, Tooltip("화면 왼쪽 아래에 배치할 머리 뼈입니다. 비우면 카메라 이동 없이 기존 줌만 적용합니다.")]
        private Transform _head;
        [SerializeField, Tooltip("조준 중 머리의 화면 위치입니다. 왼쪽 아래(0,0), 오른쪽 위(1,1)이며 X를 낮추면 왼쪽, Y를 낮추면 아래로 배치됩니다.")]
        private Vector2 _headViewport = new Vector2(0.23f, 0.28f);
        [SerializeField, Min(0.01f), Tooltip("구도 변경 중 벽과 유지할 카메라 반지름(m)입니다. 높이면 벽에서 더 멀리 멈추며 좁은 곳에서는 목표 구도에 도달하지 못할 수 있습니다.")]
        private float _collisionRadius = 0.15f;
        [SerializeField, Tooltip("구도 이동을 막는 벽·지형 레이어입니다. 플레이어 자신의 콜라이더와 Trigger는 제외합니다.")]
        private LayerMask _collisionMask = ~0;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Collider[] _overlaps = new Collider[16];
        private Vector3 _basePosition;
        private Vector3 _framedPosition;
        private bool _positionApplied;
        private float _baseFov;
        private float _baseDistance;
        private float _blend;
        private bool _active;

        /// <summary>카메라 연결을 검사합니다.</summary>
        private void Awake()
        {
            if (_controller != null && _camera != null)
            {
                return;
            }
            Debug.LogError("YPH_AimZoomView: 컨트롤러와 카메라를 연결하세요.", this);
            enabled = false;
        }

        /// <summary>조준 도중 뷰만 켜도 현재 비율을 즉시 반영합니다.</summary>
        private void OnEnable()
        {
            _controller.OnAimChanged += HandleAim;
            HandleAim(_controller.AimBlend, _controller.FocusViewport);
        }

        /// <summary>고정 조준 중 Inspector 배율 변경을 반영합니다. 평상시에는 FOV를 쓰지 않습니다.</summary>
        private void Update()
        {
            if (_active)
            {
                _camera.fieldOfView = _baseFov * Mathf.Lerp(1f, _aimFovMultiplier, _blend);
                // 기본 카메라의 LateUpdate 전에 거리를 전달해 기존 벽 충돌 검사도 함께 적용합니다.
                if (_orbitCamera != null)
                {
                    _orbitCamera.distance = Mathf.Lerp(_baseDistance, _aimDistance, _blend);
                }
            }
        }

        /// <summary>화면 위치 차이를 카메라의 좌우·상하 이동량으로 바꿉니다. 벽이 있으면 이동을 줄여 시야가 벽 안으로 들어가지 않게 합니다.</summary>
        private void LateUpdate()
        {
            RestorePosition();
            if (!_active || _head == null || _blend <= 0f)
            {
                return;
            }
            Transform cameraTransform = _camera.transform;
            Vector3 viewport = _camera.WorldToViewportPoint(_head.position);
            if (viewport.z <= _camera.nearClipPlane)
            {
                return;
            }
            float height = 2f * viewport.z * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 offset = (cameraTransform.right * ((viewport.x - _headViewport.x) * height * _camera.aspect)
                + cameraTransform.up * ((viewport.y - _headViewport.y) * height)) * _blend;
            _basePosition = cameraTransform.position;
            float distance = offset.magnitude;
            if (distance > 0.001f)
            {
                Vector3 direction = offset / distance;
                int count = Physics.SphereCastNonAlloc(_basePosition, _collisionRadius, direction, _hits, distance, _collisionMask, QueryTriggerInteraction.Ignore);
                float allowed = count == _hits.Length ? 0f : distance;
                // SphereCast는 출발 지점과 이미 겹친 벽을 찾지 못하므로 별도로 검사합니다.
                int overlapCount = Physics.OverlapSphereNonAlloc(_basePosition, _collisionRadius, _overlaps, _collisionMask, QueryTriggerInteraction.Ignore);
                if (overlapCount == _overlaps.Length)
                {
                    allowed = 0f;
                }
                for (int i = 0; i < overlapCount; i++)
                {
                    if (!_overlaps[i].transform.IsChildOf(_head.root))
                    {
                        allowed = 0f;
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    if (!_hits[i].transform.IsChildOf(_head.root))
                    {
                        allowed = Mathf.Min(allowed, Mathf.Max(0f, _hits[i].distance - 0.01f));
                    }
                }
                offset = direction * allowed;
            }
            _framedPosition = _basePosition + offset;
            cameraTransform.position = _framedPosition;
            _positionApplied = true;
        }

        /// <summary>기본 카메라가 이미 갱신됐다면 그대로 두고, 이 뷰가 마지막에 옮긴 위치만 복구합니다.</summary>
        private void RestorePosition()
        {
            if (_positionApplied && _camera != null && (_camera.transform.position - _framedPosition).sqrMagnitude < 0.00000001f)
            {
                _camera.transform.position = _basePosition;
            }
            _positionApplied = false;
        }

        /// <summary>구독을 해제하고 조준 중이었다면 원래 FOV를 한 번 복구합니다.</summary>
        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnAimChanged -= HandleAim;
            }
            Restore();
        }

        /// <summary>처음 조준하는 순간의 FOV만 저장해 반복 조준에서 줌이 누적되지 않게 합니다.</summary>
        private void HandleAim(float blend, Vector2 focus)
        {
            _blend = blend;
            if (blend <= 0f)
            {
                Restore();
                return;
            }
            if (!_active)
            {
                _baseFov = _camera.fieldOfView;
                if (_orbitCamera != null)
                {
                    _baseDistance = _orbitCamera.distance;
                }
                _active = true;
            }
            _camera.fieldOfView = _baseFov * Mathf.Lerp(1f, _aimFovMultiplier, blend);
        }

        /// <summary>우리 효과가 실제로 사용한 경우에만 복구합니다. 다른 코드가 정한 평상시 FOV는 보존합니다.</summary>
        private void Restore()
        {
            RestorePosition();
            if (!_active)
            {
                return;
            }
            if (_camera != null)
            {
                _camera.fieldOfView = _baseFov;
            }
            if (_orbitCamera != null)
            {
                _orbitCamera.distance = _baseDistance;
            }
            _active = false;
        }
    }
}
