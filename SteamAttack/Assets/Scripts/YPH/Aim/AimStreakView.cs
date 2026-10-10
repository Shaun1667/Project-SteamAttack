using UnityEngine;

namespace YPH
{
    /// <summary>YPH_AimEffects의 카메라 앞 사각형에 방사형 줄을 표시합니다. 공유 재질을 바꾸지 않고 MPB를 재사용합니다.</summary>
    public class AimStreakView : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FocusId = Shader.PropertyToID("_Focus");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int CountId = Shader.PropertyToID("_LineCount");
        private static readonly int WidthId = Shader.PropertyToID("_LineWidth");
        private static readonly int ClearId = Shader.PropertyToID("_ClearRadius");
        private static readonly int FadeId = Shader.PropertyToID("_FadeRadius");
        private static readonly int FlickerId = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int RandomId = Shader.PropertyToID("_Randomness");
        [SerializeField, Tooltip("조준 비율과 화면 초점을 알려 줄 컨트롤러입니다.")]
        private AimController _controller;
        [SerializeField, Tooltip("YPH/AimStreak 재질을 쓰는 카메라 자식 Quad입니다. 조준이 풀리면 이 렌더러를 숨깁니다.")]
        private MeshRenderer _renderer;
        [SerializeField, Range(0f, 1f), Tooltip("조준이 완전히 켜졌을 때 줄무늬 진하기입니다. 높일수록 진해지며 0이면 보이지 않습니다.")]
        private float _maxIntensity = 0.35f;
        [SerializeField, Tooltip("줄무늬 색입니다. 기본은 오방색 적입니다. 과열과 헷갈리면 흑·백으로 바꿀 수 있습니다.")]
        private Color _color = ObangColors.Jeok;
        [SerializeField, Min(8f), Tooltip("화면 둘레를 나눌 줄 개수입니다. 많을수록 줄이 촘촘하고 가늘어집니다.")]
        private float _lineCount = 90f;
        [SerializeField, Range(0.05f, 0.9f), Tooltip("각 칸에서 줄이 차지하는 비율입니다. 높일수록 굵어져 화면을 많이 덮습니다.")]
        private float _lineWidth = 0.35f;
        [SerializeField, Min(0f), Tooltip("화면 높이 기준으로 줄을 비울 중심 반경입니다. 높일수록 조준점 주변이 넓게 비어 보입니다.")]
        private float _clearRadius = 0.28f;
        [SerializeField, Min(0f), Tooltip("줄이 가장 진해지는 반경입니다. Clear Radius보다 크게 두며 높일수록 천천히 진해집니다.")]
        private float _fadeRadius = 0.65f;
        [SerializeField, Min(0f), Tooltip("줄의 깜빡임 속도입니다. 0이면 고정되고 높이면 빠르게 변합니다.")]
        private float _flickerSpeed = 6f;
        [SerializeField, Range(0f, 1f), Tooltip("줄마다 굵기와 깜빡임이 다른 정도입니다. 0이면 균일하게 고정되고 높이면 불규칙해집니다.")]
        private float _randomness = 0.6f;
        private MaterialPropertyBlock _block;
        private float _blend;
        private Vector2 _focus;

        /// <summary>연결을 검사하고 렌더러별 속성 묶음을 한 번 만듭니다.</summary>
        private void Awake()
        {
            if (_controller == null || _renderer == null)
            {
                Debug.LogError("YPH_AimStreakView: 컨트롤러와 Quad 렌더러를 연결하세요.", this);
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
        }

        /// <summary>현재 조준값을 즉시 받아 도중에 켜도 올바르게 표시합니다.</summary>
        private void OnEnable()
        {
            _controller.OnAimChanged += HandleAim;
            HandleAim(_controller.AimBlend, _controller.FocusViewport);
        }

        /// <summary>활성 조준 중에만 Inspector 변경을 반영합니다. 시간에 따른 깜빡임은 셰이더가 계산합니다.</summary>
        private void Update()
        {
            if (_blend > 0f)
            {
                Apply();
            }
        }

        /// <summary>구독을 해제하고 오버레이를 숨겨 잔상을 남기지 않습니다.</summary>
        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnAimChanged -= HandleAim;
            }
            if (_renderer != null)
            {
                _renderer.enabled = false;
            }
            _blend = 0f;
        }

        /// <summary>조준 상태·초점을 저장하고 비율이 0일 때는 드로우 자체를 끕니다.</summary>
        private void HandleAim(float blend, Vector2 focus)
        {
            _blend = blend;
            _focus = focus;
            _renderer.enabled = blend > 0f;
            if (blend > 0f)
            {
                Apply();
            }
        }

        /// <summary>이 렌더러에만 값들을 전달합니다. 잘못된 반경 순서도 셰이더 계산이 뒤집히지 않게 제한합니다.</summary>
        private void Apply()
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, _blend * _maxIntensity);
            _block.SetVector(FocusId, new Vector4(_focus.x, _focus.y, 0f, 0f));
            _block.SetColor(ColorId, _color);
            _block.SetFloat(CountId, _lineCount);
            _block.SetFloat(WidthId, _lineWidth);
            _block.SetFloat(ClearId, _clearRadius);
            _block.SetFloat(FadeId, Mathf.Max(_clearRadius + 0.001f, _fadeRadius));
            _block.SetFloat(FlickerId, _flickerSpeed);
            _block.SetFloat(RandomId, _randomness);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
