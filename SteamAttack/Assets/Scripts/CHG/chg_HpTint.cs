using UnityEngine;

/// <summary>
/// 플레이어 HP에 따라 캐릭터 전체의 채도와 명도를 서서히 낮춥니다 (HP가 줄수록 어둡고 흑백에 가까워짐 = 물빠진 느낌).
/// 캐릭터 재질이 chg_CharacterDesaturate 셰이더(_Saturation)를 써야 동작합니다. 모델 오브젝트에 붙입니다.
/// </summary>
public class chg_HpTint : MonoBehaviour
{
    public chg_PlayerController player;
    [Tooltip("HP별 채도 (인덱스 = HP). 1 = 원래 색, 0 = 흑백")]
    public float[] saturationByHp = { 0.0f, 0.15f, 0.55f, 1.0f };
    [Tooltip("HP별 명도 (인덱스 = HP). 1 = 원래 밝기, 낮을수록 어두워짐")]
    public float[] brightnessByHp = { 0.45f, 0.6f, 0.8f, 1.0f };
    [Tooltip("채도·명도가 바뀌는 속도 (초당 변화량). 낮을수록 천천히")]
    public float changeSpeed = 1.5f;

    static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;
    [Tooltip("지금 적용 중인 채도 (Play 중 확인용)")]
    [SerializeField] float _current = 1f;
    [Tooltip("지금 적용 중인 명도 (Play 중 확인용)")]
    [SerializeField] float _currentBrightness = 1f;

    void Awake()
    {
        if (!player) player = GetComponentInParent<chg_PlayerController>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb = new MaterialPropertyBlock();
        _current = Target();
        _currentBrightness = TargetBrightness();
        Apply();
    }

    float Target()
    {
        if (!player || saturationByHp == null || saturationByHp.Length == 0) return 1f;
        int i = Mathf.Clamp(player.Hp, 0, saturationByHp.Length - 1);
        return saturationByHp[i];
    }

    float TargetBrightness()
    {
        if (!player || brightnessByHp == null || brightnessByHp.Length == 0) return 1f;
        int i = Mathf.Clamp(player.Hp, 0, brightnessByHp.Length - 1);
        return brightnessByHp[i];
    }

    void LateUpdate()
    {
        float t = Target(), b = TargetBrightness();
        if (Mathf.Approximately(t, _current) && Mathf.Approximately(b, _currentBrightness)) return;
        _current = Mathf.MoveTowards(_current, t, changeSpeed * Time.deltaTime);
        _currentBrightness = Mathf.MoveTowards(_currentBrightness, b, changeSpeed * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        foreach (var r in _renderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(SaturationId, _current);
            _mpb.SetFloat(BrightnessId, _currentBrightness);
            r.SetPropertyBlock(_mpb);
        }
    }
}
