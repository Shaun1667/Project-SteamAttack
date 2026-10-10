using UnityEngine;

namespace YPH
{
    /// <summary>발사 이벤트의 총구와 끝점을 짧은 선으로 표시합니다. 선은 날아가는 탄환이 아닙니다.</summary>
    public class GunTracerView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField, Tooltip("성공한 발사를 알려 줄 총입니다. 발사 거절 시에는 선이 새로 나타나지 않습니다.")]
        private SteamGun _gun;
        [SerializeField, Tooltip("총구부터 광선 끝점까지 그릴 선입니다. 폭은 LineRenderer에서 바꿉니다.")]
        private LineRenderer _line;
        [SerializeField, Min(0.01f), Tooltip("발사 선을 유지할 초입니다. 길수록 명중 위치를 오래 볼 수 있고 현재 표시 중에도 반영됩니다.")]
        private float _showDuration = 0.1f;
        [SerializeField, Tooltip("살아 있는 적에게 피해를 준 발사의 선 색입니다.")]
        private Color _hitColor = new Color(0.3f, 1f, 0.4f, 1f);
        [SerializeField, Tooltip("허공·벽·태그 없는 대상에 쏜 발사의 선 색입니다.")]
        private Color _missColor = new Color(1f, 0.9f, 0.3f, 1f);
        private MaterialPropertyBlock _block;
        private float _elapsed;
        private bool _hit;

        private void Awake()
        {
            if (_gun == null || _line == null)
            {
                Debug.LogError("YPH_GunTracerView: 총과 LineRenderer를 연결하세요.", this);
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.enabled = false;
        }

        private void OnEnable() { _gun.OnFired += HandleFired; }

        private void OnDisable()
        {
            if (_gun != null) _gun.OnFired -= HandleFired;
            if (_line != null) _line.enabled = false;
        }

        private void Update()
        {
            if (!_line.enabled) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= _showDuration) _line.enabled = false;
            else ApplyColor();
        }

        private void HandleFired(Vector3 muzzle, Vector3 end, bool hit)
        {
            _line.SetPosition(0, muzzle);
            _line.SetPosition(1, end);
            _hit = hit;
            ApplyColor();
            _elapsed = 0f;
            _line.enabled = true;
        }

        private void ApplyColor()
        {
            // URP Unlit은 선의 정점 색을 쓰지 않을 수 있어 재질의 BaseColor로 전달합니다.
            _line.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, _hit ? _hitColor : _missColor);
            _line.SetPropertyBlock(_block);
        }
    }
}
