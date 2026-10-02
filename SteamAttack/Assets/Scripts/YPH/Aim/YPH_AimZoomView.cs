using UnityEngine;

/// <summary>YPH_AimEffects에서 조준 중에만 카메라 FOV를 조절합니다. 컨트롤러와 같은 카메라를 연결합니다.</summary>
public class YPH_AimZoomView : MonoBehaviour
{
    [SerializeField, Tooltip("조준 비율을 알려 줄 컨트롤러입니다. 없으면 동작을 멈춥니다.")]
    private YPH_AimController _controller;
    [SerializeField, Tooltip("줌을 적용할 카메라입니다. 조준 시작 당시의 시야각으로 복구합니다.")]
    private Camera _camera;
    [SerializeField, Range(0.3f, 1f), Tooltip("조준 시 원래 시야각에 곱할 배율입니다. 작을수록 확대되며 1이면 줌이 없습니다. 50도에서 0.8이면 40도입니다.")]
    private float _aimFovMultiplier = 0.8f;
    private float _baseFov;
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
        }
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
            _active = true;
        }
        _camera.fieldOfView = _baseFov * Mathf.Lerp(1f, _aimFovMultiplier, blend);
    }

    /// <summary>우리 효과가 실제로 사용한 경우에만 복구합니다. 다른 코드가 정한 평상시 FOV는 보존합니다.</summary>
    private void Restore()
    {
        if (!_active)
        {
            return;
        }
        if (_camera != null)
        {
            _camera.fieldOfView = _baseFov;
        }
        _active = false;
    }
}
