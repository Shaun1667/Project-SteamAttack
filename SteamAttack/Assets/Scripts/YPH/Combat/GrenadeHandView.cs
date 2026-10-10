using UnityEngine;

namespace YPH
{
    /// <summary>수류탄을 장착했고 소지 수가 남았을 때 손의 구를 표시합니다.</summary>
    public class GrenadeHandView : MonoBehaviour
    {
        [SerializeField, Tooltip("제작·투척으로 변하는 소지 수를 알려 줄 컴포넌트입니다.")]
        private SteamGrenadeThrower _thrower;
        [SerializeField, Tooltip("손에 든 수류탄을 표현하는 자식 구입니다. Collider 없이 만들고 이 컴포넌트의 루트와 분리합니다.")]
        private GameObject _handSphere;
        private bool _isEquipped;

        private void Awake()
        {
            if (_thrower != null && _handSphere != null && _handSphere != gameObject) return;
            Debug.LogError("YPH_GrenadeHandView: 투척 컴포넌트와 별도 자식 손 구를 연결하세요.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            _thrower.OnHeldCountChanged += HandleCountChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (_thrower != null) _thrower.OnHeldCountChanged -= HandleCountChanged;
            if (_handSphere != null && _handSphere != gameObject) _handSphere.SetActive(false);
        }

        /// <summary>입력 담당자가 장비를 바꿀 때 호출합니다. 소지 수는 건드리지 않고 표시만 다시 판단합니다.</summary>
        public void SetEquipped(bool equipped)
        {
            _isEquipped = equipped;
            if (isActiveAndEnabled) Refresh();
        }

        private void HandleCountChanged(int current, int maximum) { Refresh(); }

        private void Refresh() { _handSphere.SetActive(_isEquipped && _thrower.HeldCount > 0); }
    }
}
