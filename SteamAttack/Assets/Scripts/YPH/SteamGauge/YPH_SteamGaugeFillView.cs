using UnityEngine;

/// <summary>
/// 탱크의 잔량 비율을 증기 메시의 채움 높이와 색으로 표시합니다.
/// 활성화 시 현재값을 즉시 표시하고, 이후 압력 변경은 목표값으로 받아 Update에서 부드럽게 따라갑니다.
/// 실제 증기압은 바꾸지 않고 셰이더에 표시값만 전달합니다.
/// </summary>
public class YPH_SteamGaugeFillView : MonoBehaviour
{
    // 반복 적용할 셰이더 속성 이름을 미리 정수 ID로 바꿔 문자열 조회를 줄입니다.
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MinYId = Shader.PropertyToID("_MinY");
    private static readonly int MaxYId = Shader.PropertyToID("_MaxY");

    [Tooltip("채움 높이와 색의 기준이 되는 증기 탱크입니다. 표시할 탱크를 플레이 시작 전에 연결하세요.")]
    [SerializeField] private YPH_SteamTank _tank;
    [Tooltip("유리 안 증기 메시의 렌더러입니다. 이 대상의 높이와 색이 바뀝니다. 같은 오브젝트에 MeshFilter가 필요하며, 메시의 로컬 Y축이 위쪽이어야 합니다. 높이는 시작할 때 측정합니다.")]
    [SerializeField] private MeshRenderer _steamRenderer;
    [Tooltip("증기 높이와 색이 목표값을 따라가는 보간 시간(초)입니다. 클수록 천천히, 작을수록 빠르게 변합니다. 0이면 다음 Update에서 즉시 반영됩니다. 실제 증기 소비·회복 속도에는 영향을 주지 않습니다.")]
    [SerializeField, Min(0f)] private float _fillSmoothTime = 0.15f;
    [Tooltip("증기가 줄어들수록 가까워지는 색입니다. A(알파)를 높이면 더 불투명해집니다. 압력이 정확히 0이면 이 색과 관계없이 증기가 보이지 않습니다.")]
    [SerializeField] private Color _emptyColor = new Color(1f, 0.55f, 0.2f, 0.7f);
    [Tooltip("가득 찼을 때의 증기 색입니다. A(알파)를 높이면 더 불투명해집니다. 중간 잔량에서는 Empty Color와 이 색을 채움 비율에 따라 섞습니다.")]
    [SerializeField] private Color _fullColor = new Color(0.95f, 0.95f, 0.9f, 0.7f);

    // 공유 머티리얼을 복제·수정하지 않고 이 렌더러만의 값을 전달하는 재사용 저장소입니다.
    private MaterialPropertyBlock _block;
    // 목표 잔량과 화면에 보이는 잔량을 분리해야, 실제 소비는 즉시 처리하면서 표시만 천천히 움직일 수 있습니다.
    private float _targetFill;
    private float _displayFill;
    // SmoothDamp가 프레임 사이에 이어 쓸 보간 속도입니다. 매 프레임 초기화하지 않습니다.
    private float _fillVelocity;
    private float _minY;
    private float _maxY;

    private void Awake()
    {
        // 연결과 메시를 한 번 확인합니다. 누락되면 이 뷰만 중단하여 매 프레임 오류가 반복되지 않게 합니다.
        MeshFilter filter = _steamRenderer != null ? _steamRenderer.GetComponent<MeshFilter>() : null;
        if (_tank == null || filter == null || filter.sharedMesh == null)
        {
            Debug.LogError("증기 채움 뷰의 Tank, Steam Renderer와 MeshFilter 메시를 연결해 주세요.", this);
            enabled = false;
            return;
        }
        // 높이를 메시 자체의 로컬 Y 범위에서 읽어, 모델 교체 시 높이 숫자를 다시 맞출 필요를 줄입니다.
        // 런타임 메시 교체는 여기서 다시 측정하지 않으므로 참조는 플레이 전에 연결합니다.
        _block = new MaterialPropertyBlock();
        _minY = filter.sharedMesh.bounds.min.y;
        _maxY = filter.sharedMesh.bounds.max.y;
    }

    private void OnEnable()
    {
        // Awake의 연결 검사가 실패했다면 셰이더 값을 적용할 준비도 되어 있지 않습니다.
        if (_block == null)
        {
            return;
        }
        // 앞으로의 변경을 구독한 뒤 현재값도 직접 읽습니다. 시작부터 0에서 차오르는 연출을 피하고,
        // 잠시 껐다 켠 경우에도 비활성 중 바뀐 잔량을 즉시 따라잡습니다.
        _tank.OnPressureChanged += OnPressureChanged;
        _targetFill = _displayFill = _tank.NormalizedPressure;
        _fillVelocity = 0f;
        Apply();
    }

    private void Update()
    {
        // 잔량 표시가 멈췄으면 렌더러를 다시 갱신하지 않습니다. 증기의 일렁임은 셰이더 시간이 처리합니다.
        if (_displayFill == _targetFill)
        {
            return;
        }
        // 0초 설정은 즉시 반영하고, 그 외에는 이전 프레임의 속도를 이어 받아 목표에 접근합니다.
        _displayFill = _fillSmoothTime <= 0f ? _targetFill : Mathf.SmoothDamp(_displayFill, _targetFill, ref _fillVelocity, _fillSmoothTime);
        if (Mathf.Abs(_displayFill - _targetFill) < 0.0001f)
        {
            // 끝없이 남는 미세한 차이를 없애 다음 프레임부터 갱신을 멈춥니다. 빈 상태도 정확히 0이 됩니다.
            _displayFill = _targetFill;
            _fillVelocity = 0f;
        }
        Apply();
    }

    private void OnDisable()
    {
        // 꺼진 뷰가 계속 알림을 받거나, 재활성화 시 구독이 중복되는 것을 막습니다.
        if (_tank != null)
        {
            _tank.OnPressureChanged -= OnPressureChanged;
        }
    }

    private void OnPressureChanged(float current, float max)
    {
        // 이벤트에서는 목표만 바꿉니다. 표시값을 바로 덮어쓰면 Update의 부드러운 전환이 사라집니다.
        _targetFill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    private void Apply()
    {
        // 기존 속성 블록을 먼저 읽어 다른 코드가 넣은 속성을 보존합니다.
        // 표시 비율로 높이와 색을 함께 맞추고, 셰이더가 높이를 해석할 메시 범위도 전달합니다.
        _steamRenderer.GetPropertyBlock(_block);
        _block.SetFloat(FillId, _displayFill);
        _block.SetColor(ColorId, Color.Lerp(_emptyColor, _fullColor, _displayFill));
        _block.SetFloat(MinYId, _minY);
        _block.SetFloat(MaxYId, _maxY);
        _steamRenderer.SetPropertyBlock(_block);
    }
}
