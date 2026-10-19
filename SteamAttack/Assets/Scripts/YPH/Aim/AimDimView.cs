using UnityEngine;
using UnityEngine.UI;

namespace YPH
{
    /// <summary>조준 비율에 맞춰 화면 중앙에 원형 구멍이 있는 UI 덮개의 알파만 바꿉니다.</summary>
    public class AimDimView : MonoBehaviour
    {
        [SerializeField, Tooltip("조준 비율을 알려 줄 컨트롤러입니다. 비우면 부모에서 찾으며, 없으면 덮개를 숨깁니다.")]
        private AimController _aim;
        [SerializeField, Tooltip("화면 전체를 덮는 이미지입니다. YPH_M_AimDim 재질을 연결하며, 비우면 표시하지 않습니다.")]
        private Graphic _dimGraphic;
        [SerializeField, Range(0f, 1f), Tooltip("완전히 조준했을 때 바깥 어둡기입니다. 기획값은 0.5이며 높이면 더 어둡고 0이면 보이지 않습니다.")]
        private float _maxAlpha = 0.5f;

        /// <summary>부모 컨트롤러를 한 번 찾고 현재 조준 상태부터 표시합니다.</summary>
        private void OnEnable()
        {
            if (_aim == null)
            {
                _aim = GetComponentInParent<AimController>();
            }
            if (_aim != null)
            {
                _aim.OnAimChanged += HandleAim;
            }
            HandleAim(_aim != null ? _aim.AimBlend : 0f, Vector2.zero);
        }

        /// <summary>구독을 해제하고 비활성화한 덮개가 화면에 남지 않게 합니다.</summary>
        private void OnDisable()
        {
            if (_aim != null)
            {
                _aim.OnAimChanged -= HandleAim;
            }
            HandleAim(0f, Vector2.zero);
        }

        /// <summary>공유 재질은 유지하고 이미지 색의 알파만 바꿉니다. 초점과 무관하게 원은 화면 중앙입니다.</summary>
        private void HandleAim(float blend, Vector2 focus)
        {
            if (_dimGraphic == null)
            {
                return;
            }
            float alpha = _maxAlpha * Mathf.Clamp01(blend);
            Color color = _dimGraphic.color;
            if (color.a != alpha)
            {
                color.a = alpha;
                _dimGraphic.color = color;
            }
            bool visible = alpha > 0f;
            if (_dimGraphic.enabled != visible)
            {
                _dimGraphic.enabled = visible;
            }
        }
    }
}
