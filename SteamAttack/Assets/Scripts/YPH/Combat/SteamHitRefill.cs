using UnityEngine;

namespace YPH
{
    /// <summary>칼 적중을 증기 회복으로, 강공격 시작을 증기 지불로 바꾸는 공통 입구입니다.</summary>
    public class SteamHitRefill : MonoBehaviour
    {
        [SerializeField, Tooltip("적중 회복과 강공격 비용에 사용할 탱크입니다. 총과 같은 탱크를 연결하며, 비우면 오류를 알리고 이 컴포넌트를 끕니다.")]
        private SteamTank _tank;
        [SerializeField, Min(0f), Tooltip("칼 일반 공격으로 적 한 명을 한 번 맞혔을 때 회복하는 증기입니다. 높이면 더 많이 회복하며, 0이면 회복하지 않고 최대 압력을 넘지 않습니다. 강공격 적중은 회복하지 않습니다.")]
        private float _refillPerHit = 10f;
        [SerializeField, Min(0f), Tooltip("강공격을 시작할 때 내는 증기입니다(임시 30). 높이면 사용 부담이 커지고, 잔량이 모자라면 차감 없이 실패를 반환합니다. 0이면 무료입니다.")]
        private float _heavyAttackCost = 30f;

        /// <summary>연결 누락을 시작할 때 알립니다. 꺼진 입구는 회복을 멈추되 강공격 자체는 막지 않습니다.</summary>
        private void Awake()
        {
            if (_tank != null)
            {
                return;
            }
            Debug.LogError("YPH_SteamHitRefill: 회복할 탱크를 연결하세요.", this);
            enabled = false;
        }

        /// <summary>공격 전에 살아 있던 적의 일반 공격 적중만 회복합니다. 강공격·플레이어·태그 없는 대상은 제외합니다.</summary>
        public void NotifyHit(IDamageable target, bool heavy)
        {
            // 생존 여부는 공격 직전에 검사합니다. 여기서 다시 검사하면 적을 쓰러뜨린 마지막 타격이 누락됩니다.
            if (!isActiveAndEnabled || !CombatTags.IsEnemy(target) || heavy)
            {
                return;
            }
            RefillLightHit();
        }

        /// <summary>옛 호출용(강공격 구분 없음). NGH 연결과 수류탄 코드 삭제 뒤 제거 예정입니다.</summary>
        public void NotifyHit(IDamageable target)
        {
            NotifyHit(target, false);
        }

        /// <summary>강공격 시작 직전에 부릅니다. 부족하면 차감 없이 false이며, 꺼진 입구는 연결 문제로 공격을 막지 않도록 true입니다.</summary>
        public bool TryPayHeavyAttack()
        {
            return !isActiveAndEnabled || _tank.TryConsume(_heavyAttackCost);
        }

        /// <summary>실제 적중과 시험 메뉴가 같은 회복량·최대압력 규칙을 쓰게 합니다.</summary>
        private void RefillLightHit()
        {
            _tank.Refill(_refillPerHit);
        }

#if UNITY_EDITOR
        /// <summary>NGH 연결 전에도 회복을 확인하도록 플레이 중에만 적 검사를 생략합니다.</summary>
        [ContextMenu("시험: 약공격 적중 회복")]
        private void TestLightHit()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefillLightHit();
            }
        }

        /// <summary>플레이 중 비용 지불 결과와 남은 증기를 확인합니다. 실제 배출구도 탱크 신호에 반응합니다.</summary>
        [ContextMenu("시험: 강공격 증기 지불")]
        private void TestHeavyPayment()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }
            bool paid = TryPayHeavyAttack();
            Debug.Log("강공격 증기 지불: " + paid + ", 남은 증기: " + _tank.CurrentPressure, this);
        }
#endif
    }
}
