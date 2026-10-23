using UnityEngine;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 적에게 붙여두면 죽을 때 Kill 목표가 올라간다.
    /// 같은 오브젝트에 YPH EnemyHealth가 있으면 그 Died 이벤트로 저절로 보고하고,
    /// 없으면 적 사망 처리 스크립트에서 <see cref="ReportDeath"/> 를 부른다.
    /// </summary>
    public class QuestKillReporter : MonoBehaviour
    {
        [Tooltip("퀘스트 목표의 targetId 와 같아야 한다. 예: enemy_sugwi, boss_hyeonmu")]
        [SerializeField] private string enemyId = "";

        [SerializeField, Min(1)] private int countPerKill = 1;

        private bool reported;
        private EnemyHealth health;

        public string EnemyId => enemyId;

        private void OnEnable()
        {
            health = GetComponent<EnemyHealth>();
            if (health != null) health.Died += ReportDeath;
        }

        private void OnDisable()
        {
            if (health != null) health.Died -= ReportDeath;
        }

        /// <summary>사망 시 한 번만 보고한다.</summary>
        public void ReportDeath()
        {
            if (reported || string.IsNullOrEmpty(enemyId)) return;
            if (QuestManager.Instance == null) return;

            reported = true;
            QuestManager.Instance.ReportKill(enemyId, countPerKill);
        }
    }
}
