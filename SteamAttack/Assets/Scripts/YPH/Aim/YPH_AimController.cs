using System;
using UnityEngine;

/// <summary>카메라의 YPH_AimEffects에 붙어 조준 전환 비율과 초점을 관리합니다. 총·카메라를 연결하고 입력은 SetAiming으로 받습니다.</summary>
public class YPH_AimController : MonoBehaviour
{
    [SerializeField, Tooltip("가상 조준점을 읽을 총입니다. 연결이 없으면 동작을 멈춥니다.")]
    private YPH_SteamGun _gun;
    [SerializeField, Tooltip("조준점을 화면 좌표로 바꿀 카메라입니다. 이 효과를 붙인 카메라를 연결합니다.")]
    private Camera _camera;
    [SerializeField, Min(0f), Tooltip("조준이 완전히 켜지는 시간(초)입니다. 길수록 천천히 확대하고 0이면 즉시 전환합니다.")]
    private float _blendInTime = 0.12f;
    [SerializeField, Min(0f), Tooltip("조준이 완전히 풀리는 시간(초)입니다. 길수록 천천히 복구하고 0이면 즉시 전환합니다.")]
    private float _blendOutTime = 0.08f;
    [SerializeField, Min(0f), Tooltip("화면 초점이 목표를 따라가는 부드러움(초)입니다. 낮추면 빠르게 따라가고 0이면 즉시 맞춥니다.")]
    private float _focusSmoothTime = 0.03f;
    [SerializeField, Range(0.05f, 1f), Tooltip("완전히 조준했을 때의 감도 배율입니다. 1이면 그대로, 낮추면 느려집니다. 시험 씬에서는 방향키 회전에 적용합니다.")]
    private float _aimSensitivityMultiplier = 0.6f;
    [SerializeField, Range(0f, 1f), Tooltip("완전히 조준했을 때의 이동 속도 배율입니다. 0이면 제자리 조준입니다. 값만 제공하며 실제 이동 적용은 CHG 연결 후 수행합니다.")]
    private float _aimMoveSpeedMultiplier = 0.5f;

    private bool _targetAiming;
    private float _rawBlend;
    private float _blend;
    private Vector2 _focus = new Vector2(0.5f, 0.5f);
    private Vector2 _focusVelocity;

    /// <summary>입력이 요청한 목표 상태입니다. 연출은 AimBlend에 따라 서서히 변합니다.</summary>
    public bool IsAiming => _targetAiming;
    /// <summary>부드러운 곡선을 적용한 0~1 조준 비율입니다.</summary>
    public float AimBlend => _blend;
    /// <summary>왼쪽 아래 (0,0), 오른쪽 위 (1,1) 기준의 화면 초점입니다.</summary>
    public Vector2 FocusViewport => _focus;
    /// <summary>조준 비율에 맞춘 감도 배율입니다. 평상시에는 정확히 1입니다.</summary>
    public float SensitivityMultiplier => Mathf.Lerp(1f, _aimSensitivityMultiplier, _blend);
    /// <summary>후속 이동 어댑터가 읽을 배율입니다. 이 컴포넌트는 플레이어 속도를 직접 바꾸지 않습니다.</summary>
    public float MoveSpeedMultiplier => Mathf.Lerp(1f, _aimMoveSpeedMultiplier, _blend);
    /// <summary>비율이나 초점이 달라지면 새 값을 알립니다. 비활성화 시에는 0과 중앙으로 복구를 알립니다.</summary>
    public event Action<float, Vector2> OnAimChanged;

    /// <summary>필수 연결을 검사합니다. 총 아래에 붙이지 않아 장비를 넣어도 복구할 수 있어야 합니다.</summary>
    private void Awake()
    {
        if (_gun != null && _camera != null)
        {
            return;
        }
        Debug.LogError("YPH_AimController: 총과 카메라를 연결하세요.", this);
        enabled = false;
    }

    /// <summary>활성 조준·복구 중에만 비율과 초점을 계산합니다.</summary>
    private void Update()
    {
        if (!_targetAiming && _rawBlend == 0f)
        {
            return;
        }
        float previousBlend = _blend;
        Vector2 previousFocus = _focus;
        float duration = _targetAiming ? _blendInTime : _blendOutTime;
        float target = _targetAiming ? 1f : 0f;
        _rawBlend = duration <= 0f ? target : Mathf.MoveTowards(_rawBlend, target, Time.deltaTime / duration);
        _blend = Mathf.SmoothStep(0f, 1f, _rawBlend);

        // 화면 밖으로 움직인 조준점은 가장자리로 제한하고, 카메라 뒤면 이전 초점을 유지합니다.
        if (_blend > 0f && _gun.TryGetAimPoint(out Vector3 point))
        {
            Vector3 viewport = _camera.WorldToViewportPoint(point);
            if (viewport.z > 0f)
            {
                Vector2 focus = new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y));
                _focus = _focusSmoothTime <= 0f ? focus : Vector2.SmoothDamp(_focus, focus, ref _focusVelocity, _focusSmoothTime);
            }
        }
        if (_blend == 0f)
        {
            _focus = new Vector2(0.5f, 0.5f);
            _focusVelocity = Vector2.zero;
        }
        if (_blend != previousBlend || _focus != previousFocus)
        {
            OnAimChanged?.Invoke(_blend, _focus);
        }
    }

    /// <summary>꺼지거나 씬을 나갈 때 연출과 외부 배율이 남지 않게 즉시 초기 상태를 알립니다.</summary>
    private void OnDisable()
    {
        _targetAiming = false;
        _rawBlend = _blend = 0f;
        _focus = new Vector2(0.5f, 0.5f);
        _focusVelocity = Vector2.zero;
        OnAimChanged?.Invoke(0f, _focus);
    }

    /// <summary>조준 목표만 바꿉니다. 같은 값을 매 프레임 전달해도 전환 시간을 다시 시작하지 않습니다.</summary>
    public void SetAiming(bool aiming)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        _targetAiming = aiming;
    }
}
