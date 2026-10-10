using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 적에게 붙여두고, 죽을 때 <see cref="ReportDeath"/> 를 호출하면
    /// Kill 목표가 올라간다. (적 사망 처리 스크립트에서 호출)
    /// </summary>
    public class QuestKillReporter : MonoBehaviour
    {
        [Tooltip("퀘스트 목표의 targetId 와 같아야 한다. 예: enemy_sugwi, boss_hyeonmu")]
        [SerializeField] private string enemyId = "";

        [SerializeField, Min(1)] private int countPerKill = 1;

        private bool reported;

        public string EnemyId => enemyId;

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
