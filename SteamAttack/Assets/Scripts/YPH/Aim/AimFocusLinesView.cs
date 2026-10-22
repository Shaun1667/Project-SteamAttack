using UnityEngine;
using UnityEngine.UI;

namespace YPH
{
    /// <summary>선택한 등장 방식으로 먹선을 표시하고 실제 조준점에 흰 조준선을 그립니다.</summary>
    public class AimFocusLinesView : MonoBehaviour
    {
        private enum RevealMode
        {
            [InspectorName("세 곳에서 그리기")] ThreePoints = 0,
            [InspectorName("전체 페이드 인")] Fade = 1
        }

        private static readonly int ModeId = Shader.PropertyToID("_RevealMode");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int BlendId = Shader.PropertyToID("_AimBlend");
        private static readonly int FocusId = Shader.PropertyToID("_Focus");
        private static readonly int InkId = Shader.PropertyToID("_InkColor");
        private static readonly int ReticleId = Shader.PropertyToID("_ReticleColor");
        private static readonly int RadiusId = Shader.PropertyToID("_ReticleRadius");
        private static readonly int ThicknessId = Shader.PropertyToID("_ReticleThickness");

        [SerializeField, Tooltip("조준 상태와 실제 조준점의 화면 위치를 읽습니다. 기존 AimController를 연결합니다.")]
        private AimController _controller;
        [SerializeField, Tooltip("YPH/UI/AimFocusLines 재질을 사용하는 화면 전체 RawImage입니다. Raycast Target은 끄세요.")]
        private RawImage _graphic;
        [SerializeField, Tooltip("전체 페이드 인은 먹선 전체가 투명한 상태에서 서서히 진해집니다. 세 곳에서 그리기는 기존처럼 세 붓끝을 따라 나타납니다.")]
        private RevealMode _revealMode = RevealMode.ThreePoints;
        [SerializeField, Min(0f), Tooltip("선택한 방식으로 먹선이 완전히 나타나는 시간(초)입니다. 길수록 천천히 나타나고 0이면 즉시 완성됩니다. 줌 전환 시간과는 별개입니다.")]
        private float _drawDuration = 0.55f;
        [SerializeField, Tooltip("붓칠 색과 최대 진하기입니다. 알파를 낮추면 배경이 더 비치며 0이면 먹선이 숨겨집니다.")]
        private Color _inkColor = Color.black;
        [SerializeField, Tooltip("조준선 색입니다. 알파를 낮추면 흐려지고 0이면 조준선만 숨겨집니다.")]
        private Color _reticleColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField, Min(2f), Tooltip("화면 높이 1080 기준 조준원 반지름(px)입니다. 높이면 원이 커지며 화면 크기에 비례해 표시됩니다.")]
        private float _reticleRadius = 18f;
        [SerializeField, Min(0.5f), Tooltip("화면 높이 1080 기준 조준선 두께(px)입니다. 높이면 선이 굵어집니다.")]
        private float _reticleThickness = 1.4f;

        private Material _material;
        private Material _sourceMaterial;
        private float _reveal;
        private bool _wasAiming;

        /// <summary>공유 재질을 덮어쓰지 않도록 이 화면 전용 재질을 한 번 만듭니다.</summary>
        private void Awake()
        {
            if (_controller == null || _graphic == null || _graphic.material == null
                || _graphic.material.shader.name != "YPH/UI/AimFocusLines" || _graphic.material.GetTexture("_InkTex") == null)
            {
                Debug.LogError("AimFocusLinesView: 조준 컨트롤러·RawImage·YPH/UI/AimFocusLines 재질과 Ink Border 텍스처를 연결하세요.", this);
                enabled = false;
                return;
            }
            _sourceMaterial = _graphic.material;
            _material = new Material(_sourceMaterial);
            _graphic.material = _material;
            _graphic.raycastTarget = false;
            _graphic.enabled = false;
        }

        /// <summary>조준 비율이 1에 멈춘 뒤에도 붓칠은 계속 진행해야 하므로 매 프레임 진행도를 따로 계산합니다.</summary>
        private void LateUpdate()
        {
            bool aiming = _controller.IsAiming;
            if (aiming && !_wasAiming)
            {
                _reveal = 0f;
            }
            if (aiming)
            {
                _reveal = _drawDuration <= 0f ? 1f : Mathf.MoveTowards(_reveal, 1f, Time.deltaTime / _drawDuration);
            }
            _wasAiming = aiming;
            float blend = _controller.AimBlend;
            _graphic.enabled = blend > 0f;
            if (blend <= 0f)
            {
                _reveal = 0f;
                return;
            }
            Vector2 focus = _controller.FocusViewport;
            _material.SetFloat(ModeId, (float)_revealMode);
            _material.SetFloat(RevealId, _reveal);
            _material.SetFloat(BlendId, blend);
            _material.SetVector(FocusId, new Vector4(focus.x, focus.y, 0f, 0f));
            _material.SetColor(InkId, _inkColor);
            _material.SetColor(ReticleId, _reticleColor);
            _material.SetFloat(RadiusId, _reticleRadius);
            _material.SetFloat(ThicknessId, _reticleThickness);
        }

        /// <summary>비활성화할 때 잔상을 지우고 다음 조준은 처음부터 그립니다.</summary>
        private void OnDisable()
        {
            if (_graphic != null)
            {
                _graphic.enabled = false;
            }
            _reveal = 0f;
            _wasAiming = false;
        }

        /// <summary>실행 중 만든 재질만 해제하고 에셋 참조는 되돌려 줍니다.</summary>
        private void OnDestroy()
        {
            if (_graphic != null && _graphic.material == _material)
            {
                _graphic.material = _sourceMaterial;
            }
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
