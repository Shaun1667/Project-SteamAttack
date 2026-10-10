using UnityEngine;

namespace YPH
{
    /// <summary>폭발 반경을 잠시 보여 준 뒤 사라지는 개발용 구입니다. 피해 판정과 콜라이더가 없습니다.</summary>
    public class ExplosionFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField, Tooltip("투명 URP Unlit 재질을 쓰는 구 렌더러입니다.")]
        private Renderer _renderer;
        [SerializeField, Min(0.01f), Tooltip("구가 완전히 투명해지고 삭제되기까지의 초입니다. 클수록 반경을 오래 볼 수 있습니다.")]
        private float _duration = 0.2f;
        [SerializeField, Range(0f, 1f), Tooltip("폭발 직후 불투명도입니다. 0이면 보이지 않고 1이면 처음에 완전히 불투명합니다.")]
        private float _startAlpha = 0.35f;
        private MaterialPropertyBlock _block;
        private Color _color;
        private float _elapsed;
        private bool _showing;

        private void Awake()
        {
            if (_renderer == null)
            {
                Debug.LogError("YPH_ExplosionFlash: 구 렌더러를 연결하세요.", this);
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _color = _renderer.sharedMaterial.GetColor(BaseColorId);
        }

        /// <summary>단위 구의 지름을 반경의 두 배로 맞추고 표시를 시작합니다.</summary>
        public void Show(float radius)
        {
            if (!isActiveAndEnabled) return;
            if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                Debug.LogError("YPH_ExplosionFlash: 반경은 유한한 양수여야 합니다.", this);
                return;
            }
            transform.localScale = Vector3.one * (radius * 2f);
            _elapsed = 0f;
            _showing = true;
            ApplyAlpha();
        }

        private void Update()
        {
            if (!_showing) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration) Destroy(gameObject);
            else ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            _color.a = _startAlpha * (1f - Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration)));
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, _color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
