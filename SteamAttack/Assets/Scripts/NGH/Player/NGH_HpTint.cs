using UnityEngine;

/// <summary>
/// 플레이어 HP에 따라 캐릭터 옷감 색이 물빠지듯 바래게 합니다 (옅은 곳부터 빠지고 진한 먹선은 마지막까지 남음, 그림자·굴곡은 유지).
/// 바래는 방식의 세부 값은 재질(chg_mat_body)의 Wash Out 항목에서 조절합니다.
/// chg_CharacterDesaturate 셰이더를 쓰는 재질에만 적용됩니다 (다른 재질 — 예: 증기통 — 은 그대로). 모델 오브젝트에 붙입니다.
/// </summary>
public class NGH_HpTint : MonoBehaviour
{
    public NGH_PlayerController player;
    [Tooltip("HP별 채도 (인덱스 = HP). 1 = 원래 색, 0 = 흑백")]
    public float[] saturationByHp = { 0.0f, 0.2f, 0.7f, 1.0f };
    [Tooltip("HP별 명도 (인덱스 = HP). 1 = 원래 밝기, 낮을수록 어두워짐")]
    public float[] brightnessByHp = { 1.0f, 1.0f, 1.0f, 1.0f };
    [Tooltip("HP별 물빠짐 양 (인덱스 = HP). 0 = 원래 색, 1 = 진한 먹선까지 모두 종이색. 옅은 곳부터 차례로 빠짐")]
    public float[] fadeByHp = { 0.88f, 0.72f, 0.45f, 0.0f };
    [Tooltip("빛이 바랠 때 섞이는 색 (종이색)")]
    public Color fadeColor = new Color(0.93f, 0.91f, 0.87f, 1f);
    [Tooltip("채도·명도가 바뀌는 속도 (초당 변화량). 낮을수록 천천히")]
    public float changeSpeed = 1.5f;

    static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int FadeColorId = Shader.PropertyToID("_FadeColor");
    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;
    [Tooltip("지금 적용 중인 채도 (Play 중 확인용)")]
    [SerializeField] float _current = 1f;
    [Tooltip("지금 적용 중인 명도 (Play 중 확인용)")]
    [SerializeField] float _currentBrightness = 1f;
    [Tooltip("지금 적용 중인 빛바램 (Play 중 확인용)")]
    [SerializeField] float _currentFade = 0f;

    void Awake()
    {
        if (!player) player = GetComponentInParent<NGH_PlayerController>();
        // 이 셰이더(_Saturation)를 쓰는 렌더러만 — 증기통 등 다른 재질은 제외
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r && r.sharedMaterial && r.sharedMaterial.HasProperty(SaturationId)) list.Add(r);
        _renderers = list.ToArray();
        _mpb = new MaterialPropertyBlock();
        _current = Target();
        _currentBrightness = TargetBrightness();
        _currentFade = TargetFade();
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

    float TargetFade()
    {
        if (!player || fadeByHp == null || fadeByHp.Length == 0) return 0f;
        int i = Mathf.Clamp(player.Hp, 0, fadeByHp.Length - 1);
        return fadeByHp[i];
    }

    void LateUpdate()
    {
        float t = Target(), b = TargetBrightness(), f = TargetFade();
        if (Mathf.Approximately(t, _current) && Mathf.Approximately(b, _currentBrightness) && Mathf.Approximately(f, _currentFade)) return;
        _current = Mathf.MoveTowards(_current, t, changeSpeed * Time.deltaTime);
        _currentBrightness = Mathf.MoveTowards(_currentBrightness, b, changeSpeed * Time.deltaTime);
        _currentFade = Mathf.MoveTowards(_currentFade, f, changeSpeed * Time.deltaTime);
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
            _mpb.SetFloat(FadeId, _currentFade);
            _mpb.SetColor(FadeColorId, fadeColor);
            r.SetPropertyBlock(_mpb);
        }
    }
}
