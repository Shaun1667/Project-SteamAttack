using UnityEngine;

namespace YPH
{
    /// <summary>적 명중 한 번을 증기 회복량으로 바꾸는 공통 입구입니다.</summary>
    public class SteamHitRefill : MonoBehaviour
    {
        [SerializeField, Tooltip("적을 맞혔을 때 채울 탱크입니다. 총과 수류탄이 소비하는 탱크와 같아야 합니다.")]
        private SteamTank _tank;
        [SerializeField, Min(0f), Tooltip("적 한 명을 한 번 맞혔을 때 회복하는 증기입니다. 0이면 회복하지 않으며 최대 압력을 넘지 않습니다.")]
        private float _refillPerHit = 10f;

        private void Awake()
        {
            if (_tank != null) return;
            Debug.LogError("YPH_SteamHitRefill: 회복할 탱크를 연결하세요.", this);
            enabled = false;
        }

        /// <summary>공격 전에 살아 있던 적을 맞혔을 때 호출합니다. 플레이어·태그 없는 대상은 회복하지 않습니다.</summary>
        public void NotifyHit(IDamageable target)
        {
            // 생존 여부는 공격 직전에 검사합니다. 여기서 다시 검사하면 적을 쓰러뜨린 마지막 타격이 누락됩니다.
            if (!isActiveAndEnabled || !CombatTags.IsEnemy(target)) return;
            _tank.Refill(_refillPerHit);
        }
    }
}
