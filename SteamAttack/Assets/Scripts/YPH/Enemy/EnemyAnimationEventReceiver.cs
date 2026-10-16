using UnityEngine;

namespace YPH
{
    /// <summary>Animator 오브젝트에서 기존 OnAttackHit 이벤트를 받습니다. 공격 판정을 두 번 만들지 않습니다.</summary>
    public class EnemyAnimationEventReceiver : MonoBehaviour
    {
        private EnemyAI _owner;
        internal void Bind(EnemyAI owner) => _owner = owner;

        /// <summary>동심원 예고에 쓴 시각과 실제 애니메이션 시각을 개발용 로그로 비교합니다.</summary>
        public void OnAttackHit(string attackName)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_owner != null && DebugView.Enabled)
            {
                _owner.Log("타격 이벤트 울림: " + attackName);
            }
#endif
        }
    }
}
