using UnityEngine;

namespace YPH
{
    /// <summary>총의 발사·재장전 단계를 몸체 색으로 보여 줍니다. 증기 잔량과 발사 가능 여부는 표시하지 않습니다.</summary>
    public class GunStateView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField, Tooltip("현재 단계를 읽고 변경 이벤트를 받을 총입니다.")]
        private SteamGun _gun;
        [SerializeField, Tooltip("상태 색을 표시할 몸체입니다. 공유 재질 대신 이 렌더러에만 색을 적용합니다.")]
        private Renderer _renderer;
        [SerializeField, Tooltip("발사 준비 상태의 색입니다. 실행 중 변경도 현재 상태에 반영됩니다.")]
        private Color _readyColor = new Color(0.3f, 1f, 0.4f, 1f);
        [SerializeField, Tooltip("발사 직후 간격을 기다리는 동안의 색입니다.")]
        private Color _firingColor = new Color(1f, 0.3f, 0.2f, 1f);
        [SerializeField, Tooltip("재장전 중 색입니다. 실행 중 변경도 현재 상태에 반영됩니다.")]
        private Color _chamberingColor = new Color(0.3f, 0.6f, 1f, 1f);
        private MaterialPropertyBlock _block;
        private Color _shownColor;
        private bool _hasColor;

        private void Awake()
        {
            if (_gun == null || _renderer == null)
            {
                Debug.LogError("YPH_GunStateView: 총과 몸체 렌더러를 연결하세요.", this);
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _gun.OnStateChanged += HandleStateChanged;
            ApplyColor(); // 다시 꺼낸 장비는 이벤트를 기다리지 않고 현재 색부터 보여 줍니다.
        }

        private void OnDisable()
        {
            if (_gun != null) _gun.OnStateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            ApplyColor(); // Inspector 색 변경을 감지하되 실제 렌더러 갱신은 색이 바뀔 때만 합니다.
        }

        private void HandleStateChanged(SteamGun.State previous, SteamGun.State current)
        {
            ApplyColor();
        }

        private void ApplyColor()
        {
            Color color = _gun.CurrentState switch
            {
                SteamGun.State.Firing => _firingColor,
                SteamGun.State.Chambering => _chamberingColor,
                _ => _readyColor
            };
            if (_hasColor && _shownColor == color) return;
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_block);
            _shownColor = color;
            _hasColor = true;
        }
    }
}
