using System.Text;
using TMPro;
using UnityEngine;

namespace NMJ
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

        [Tooltip("보스전 중(현무 체력바가 보이는 동안)에는 퀘스트창을 숨긴다. 둘 다 화면 위 가운데라 겹친다.")]
        [SerializeField] private bool hideDuringBossFight = true;
        [Tooltip("숨기고 다시 보일 때 서서히 바뀌는 빠르기 (초당 투명도)")]
        [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;

        private readonly StringBuilder builder = new StringBuilder();
        private QuestManager manager;
        private CanvasGroup fadeGroup;

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

        private void Update()
        {
            if (root == null) return;
            if (fadeGroup == null)
            {
                fadeGroup = root.GetComponent<CanvasGroup>();
                if (fadeGroup == null) fadeGroup = root.AddComponent<CanvasGroup>();
            }

            // 보스전·보스 처치 문구·엔딩 크레딧 중이면 비켜 준다 (일시정지 중에도 바뀌도록 unscaled 시간 사용)
            float target = hideDuringBossFight && (HyeonmuBossBar.AnyVisible || BossDefeatBanner.IsShowing || EndingCredits.IsPlaying) ? 0f : 1f;
            fadeGroup.alpha = Mathf.MoveTowards(fadeGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
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
