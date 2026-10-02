using System.Text;
using SteamAttack.Quests;
using TMPro;
using UnityEngine;

namespace SteamAttack.UI
{
    /// <summary>
    /// HUD 한쪽에 추적 중인 퀘스트(기본값: 진행 중인 메인 퀘스트)의
    /// 제목과 목표 달성도를 표시한다.
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text objectiveText;
        [SerializeField] private TMP_Text stateText;

        [Tooltip("메인 퀘스트 제목 앞에 붙일 표식.")]
        [SerializeField] private string mainQuestPrefix = "[어명] ";

        private readonly StringBuilder builder = new StringBuilder();
        private QuestManager manager;

        private void Start()
        {
            manager = QuestManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[QuestTrackerUI] QuestManager 가 없다.");
                return;
            }

            manager.QuestAccepted += OnQuestChanged;
            manager.QuestStatusChanged += OnQuestChanged;
            manager.QuestCompleted += OnQuestChanged;
            manager.ObjectiveProgressed += OnObjectiveProgressed;

            Redraw();
        }

        private void OnDestroy()
        {
            if (manager == null) return;

            manager.QuestAccepted -= OnQuestChanged;
            manager.QuestStatusChanged -= OnQuestChanged;
            manager.QuestCompleted -= OnQuestChanged;
            manager.ObjectiveProgressed -= OnObjectiveProgressed;
        }

        private void OnQuestChanged(QuestProgress _) => Redraw();
        private void OnObjectiveProgressed(QuestProgress _, int __) => Redraw();

        public void Redraw()
        {
            var tracked = manager != null ? (manager.TrackedQuest ?? manager.GetActiveMainQuest()) : null;
            bool show = tracked != null && tracked.Data != null && tracked.Status != QuestStatus.Completed;

            if (root != null) root.SetActive(show);
            if (!show) return;

            if (titleText != null)
            {
                string prefix = tracked.Data.Category == QuestCategory.Main ? mainQuestPrefix : string.Empty;
                titleText.text = prefix + tracked.Data.Title;
            }

            if (objectiveText != null)
            {
                builder.Clear();
                for (int i = 0; i < tracked.ObjectiveCount; i++)
                {
                    if (i > 0) builder.AppendLine();
                    builder.Append(tracked.GetObjectiveLine(i));
                }

                objectiveText.text = builder.ToString();
            }

            if (stateText != null)
                stateText.text = tracked.Status == QuestStatus.ReadyToComplete ? "의뢰인에게 보고" : string.Empty;
        }
    }
}
