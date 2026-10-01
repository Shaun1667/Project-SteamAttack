using UnityEngine;

/// <summary>
/// 탱크의 잔량 비율을 빈 각도와 가득 찬 각도 사이의 바늘 회전으로 표시합니다.
/// 압력이 바뀌면 목표 각도를 갱신하고, 매 프레임 그 각도를 따라가며 낮은 압력에서만 떨림을 더합니다.
/// </summary>
public class YPH_SteamNeedleView : MonoBehaviour
{
    [Tooltip("바늘이 표시할 증기 탱크입니다. 기준 탱크를 플레이 시작 전에 연결하세요.")]
    [SerializeField] private YPH_SteamTank _tank;
    [Tooltip("바늘의 회전 중심입니다. 바늘 메시를 자식으로 둔 피벗을 연결하세요. 시작할 때 이 피벗의 초기 회전을 저장하고, 그 방향을 기준으로 각도를 더합니다.")]
    [SerializeField] private Transform _needlePivot;
    [Tooltip("바늘이 회전할 로컬 축입니다. (0,0,1)은 Z축, (1,0,0)은 X축입니다. 부호를 반대로 하면 회전 방향이 뒤집힙니다. 벡터 길이는 회전 속도에 영향을 주지 않으며 (0,0,0)은 사용할 수 없습니다.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;
    [Tooltip("압력이 0일 때 바늘의 목표 각도(도)입니다. 초기 회전에 이 각도를 더합니다. Full Angle과의 차이를 키우면 전체 회전 범위가 넓어집니다. 실행 중 변경은 다음 압력 변경 또는 뷰 재활성화 때 목표에 반영됩니다.")]
    [SerializeField] private float _emptyAngle = 90f;
    [Tooltip("압력이 가득 찼을 때 바늘의 목표 각도(도)입니다. Empty Angle보다 작게 설정하면 압력이 증가할 때 도는 방향이 반대가 됩니다. 실행 중 변경은 다음 압력 변경 또는 뷰 재활성화 때 목표에 반영됩니다.")]
    [SerializeField] private float _fullAngle = -90f;
    [Tooltip("바늘이 목표 각도를 따라가는 보간 시간(초)입니다. 클수록 느리고 부드럽게, 작을수록 빠르게 움직입니다. 0이면 다음 Update에서 목표 각도로 바로 이동합니다.")]
    [SerializeField, Min(0f)] private float _smoothTime = 0.2f;
    [Tooltip("바늘이 떨리기 시작하는 잔량 비율입니다. 0.2는 20% 미만에서 떨린다는 뜻입니다. 높일수록 더 많은 증기가 남아 있어도 떨리며, 0이면 떨리지 않습니다. 경계값과 같을 때는 떨리지 않습니다.")]
    [SerializeField, Range(0f, 1f)] private float _jitterThreshold = 0.2f;
    [Tooltip("낮은 압력에서 바늘이 양쪽으로 흔들리는 최대 각도(도)입니다. 1.5이면 기준 각도에서 최대 ±1.5도 흔들립니다. 클수록 크게 떨리고 0이면 떨림이 사라집니다.")]
    [SerializeField, Min(0f)] private float _jitterAmplitude = 1.5f;
    [Tooltip("낮은 압력에서 바늘이 떨리는 빠르기입니다. 클수록 자주 떨리고 0이면 떨리지 않습니다. 값은 라디안/초이며, 약 6.28이면 초당 한 번 왕복합니다.")]
    [SerializeField, Min(0f)] private float _jitterSpeed = 40f;

    // 모델이 원래 향하던 방향을 보존할 기준 회전입니다. 표시 각도는 이 기준에 덧붙입니다.
    private Quaternion _initialRotation;
    // 목표 각도와 표시 각도를 분리하고, SmoothDamp의 속도를 프레임 사이에 유지합니다.
    private float _targetAngle;
    private float _displayAngle;
    private float _angleVelocity;
    // 각도 계산과 떨림 조건에 사용하는 0~1 잔량 비율입니다.
    private float _pressure;

    private void Awake()
    {
        // 회전 대상이나 기준 탱크가 없으면 이 뷰만 중단합니다. 영벡터는 회전축으로 쓸 수 없습니다.
        if (_tank == null || _needlePivot == null || _rotationAxis.sqrMagnitude == 0f)
        {
            Debug.LogError("압력 바늘의 Tank, Needle Pivot과 0이 아닌 회전축을 지정해 주세요.", this);
            enabled = false;
            return;
        }
        // 이후 회전이 누적되지 않도록, 아직 표시 각도를 적용하지 않은 원래 자세를 한 번 저장합니다.
        _initialRotation = _needlePivot.localRotation;
    }

    private void OnEnable()
    {
        // 비활성 중 변한 값도 반영하려고 구독 직후 현재 압력을 직접 읽습니다.
        // 첫 표시에는 보간과 떨림을 넣지 않고 현재 잔량의 정확한 각도부터 보여 줍니다.
        if (_tank == null || _needlePivot == null)
        {
            return;
        }
        _tank.OnPressureChanged += OnPressureChanged;
        OnPressureChanged(_tank.CurrentPressure, _tank.MaxPressure);
        _displayAngle = _targetAngle;
        _angleVelocity = 0f;
        Apply(0f);
    }

    private void Update()
    {
        // 기본 프록시를 앞(-Z)에서 보면 -90도는 오른쪽, 90도는 왼쪽입니다. 잔량 감소 시 위쪽 0도를 지나갑니다.
        // 원형 최단 경로 보간을 쓰면 압력계의 반대편을 가로질러 잘못된 눈금을 지나갈 수 있습니다.
        _displayAngle = _smoothTime <= 0f ? _targetAngle : Mathf.SmoothDamp(_displayAngle, _targetAngle, ref _angleVelocity, _smoothTime);
        // 떨림은 표시할 때만 더하고 목표 각도·보간 속도에는 섞지 않아, 낮은 압력이 끝나면 남지 않게 합니다.
        float jitter = _pressure < _jitterThreshold ? Mathf.Sin(Time.time * _jitterSpeed) * _jitterAmplitude : 0f;
        Apply(jitter);
    }

    private void OnDisable()
    {
        // 재활성화할 때 같은 압력 알림이 여러 번 들어오지 않도록 구독을 정리합니다.
        if (_tank != null)
        {
            _tank.OnPressureChanged -= OnPressureChanged;
        }
    }

    private void OnPressureChanged(float current, float max)
    {
        // 압력을 0~1 비율로 바꾼 뒤 두 끝 각도 사이에 대응시킵니다. 최대값이 잘못되어도 0으로 처리합니다.
        _pressure = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        _targetAngle = Mathf.Lerp(_emptyAngle, _fullAngle, _pressure);
    }

    private void Apply(float jitter)
    {
        // 매번 초기 자세에서 다시 계산합니다. 현재 회전에 계속 더하는 방식의 누적 회전을 피합니다.
        _needlePivot.localRotation = _initialRotation * Quaternion.AngleAxis(_displayAngle + jitter, _rotationAxis);
    }
}
