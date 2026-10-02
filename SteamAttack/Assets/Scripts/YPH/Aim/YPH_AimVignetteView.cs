using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>YPH_AimEffects의 전용 Volume에 조준 비네트를 적용합니다. 공유 프로파일 대신 런타임 복사본을 사용합니다.</summary>
public class YPH_AimVignetteView : MonoBehaviour
{
    [SerializeField, Tooltip("조준 비율과 화면 초점을 알려 줄 컨트롤러입니다.")]
    private YPH_AimController _controller;
    [SerializeField, Tooltip("Vignette 하나가 있는 전용 전역 볼륨입니다. 카메라의 후처리를 켜고 이 볼륨 레이어를 포함해야 보입니다.")]
    private Volume _volume;
    [SerializeField, Range(0f, 1f), Tooltip("완전히 조준했을 때 가장자리가 어두워지는 정도입니다. 0이면 어두워지지 않고 높일수록 강해집니다.")]
    private float _maxIntensity = 0.45f;
    [SerializeField, Range(0.01f, 1f), Tooltip("어두운 가장자리와 밝은 중앙 사이 경계의 부드러움입니다. 높이면 경계가 넓게 번집니다.")]
    private float _smoothness = 0.6f;
    [SerializeField, Tooltip("켜면 원형 비네트, 끄면 화면 가로세로 비율을 따르는 모양입니다.")]
    private bool _rounded;
    [SerializeField, Tooltip("가장자리를 어둡게 덮을 색입니다. 기본은 오방색 흑이며 색을 밝히면 먹색 느낌이 줄어듭니다.")]
    private Color _color = YPH_ObangColors.Heuk;
    private VolumeProfile _profile;
    private Vignette _vignette;
    private bool _ownsProfile;
    private float _blend;
    private Vector2 _focus;

    /// <summary>공유 에셋에 Play 값이 저장되지 않도록 인스턴스 프로파일을 한 번만 가져옵니다.</summary>
    private void Awake()
    {
        if (_controller != null && _volume != null && _volume.sharedProfile != null)
        {
            _ownsProfile = !_volume.HasInstantiatedProfile();
            _profile = _volume.profile;
            if (_profile.TryGet(out _vignette))
            {
                return;
            }
        }
        Debug.LogError("YPH_AimVignetteView: 컨트롤러와 Vignette가 있는 볼륨을 연결하세요.", this);
        enabled = false;
    }

    /// <summary>구독 즉시 현재 조준 상태를 적용합니다.</summary>
    private void OnEnable()
    {
        _controller.OnAimChanged += HandleAim;
        HandleAim(_controller.AimBlend, _controller.FocusViewport);
    }

    /// <summary>활성 조준 중에만 Inspector 색·세기 변경을 반영합니다.</summary>
    private void Update()
    {
        if (_blend > 0f)
        {
            Apply();
        }
    }

    /// <summary>우리 오버라이드를 모두 꺼서 다른 볼륨의 비네트가 다시 적용되게 합니다.</summary>
    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnAimChanged -= HandleAim;
        }
        _blend = 0f;
        if (_vignette != null)
        {
            _vignette.SetAllOverridesTo(false);
        }
    }

    /// <summary>이 뷰가 만든 복사본만 해제합니다. 프로젝트 에셋이나 다른 소유자의 프로파일은 삭제하지 않습니다.</summary>
    private void OnDestroy()
    {
        if (!_ownsProfile || _profile == null)
        {
            return;
        }
        foreach (VolumeComponent component in _profile.components)
        {
            Destroy(component);
        }
        if (_volume != null)
        {
            _volume.profile = null;
        }
        Destroy(_profile);
    }

    /// <summary>변경된 초점과 세기를 캐시하고, 해제 이벤트에서는 오버라이드를 즉시 끕니다.</summary>
    private void HandleAim(float blend, Vector2 focus)
    {
        _blend = blend;
        _focus = focus;
        if (blend <= 0f)
        {
            _vignette.SetAllOverridesTo(false);
            return;
        }
        Apply();
    }

    /// <summary>전용 복사본의 비네트만 조절합니다.</summary>
    private void Apply()
    {
        _vignette.intensity.Override(_blend * _maxIntensity);
        _vignette.center.Override(_focus);
        _vignette.smoothness.Override(_smoothness);
        _vignette.rounded.Override(_rounded);
        _vignette.color.Override(_color);
    }
}
