using UnityEngine;

namespace SteamAttack.Quests
{
    /// <summary>플레이어가 들어오면 Reach 목표를 채우는 트리거 영역.</summary>
    [RequireComponent(typeof(Collider))]
    public class QuestZoneTrigger : MonoBehaviour
    {
        [Tooltip("퀘스트 목표의 targetId. 예: loc_bukhansan_altar")]
        [SerializeField] private string locationId = "";

        [SerializeField] private string playerTag = "Player";
        [SerializeField] private bool onlyOnce = true;

        private bool fired;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (fired && onlyOnce) return;
            if (!other.CompareTag(playerTag)) return;
            if (QuestManager.Instance == null || string.IsNullOrEmpty(locationId)) return;

            fired = true;
            QuestManager.Instance.ReportReach(locationId);
        }
    }
}
