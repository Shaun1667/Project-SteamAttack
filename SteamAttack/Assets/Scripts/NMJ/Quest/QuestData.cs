using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>퀘스트 원본 데이터(ScriptableObject).</summary>
    [CreateAssetMenu(fileName = "Quest_", menuName = "SteamAttack/Quest Data", order = 10)]
    public class QuestData : ScriptableObject
    {
        [Header("식별")]
        [SerializeField] private string questId = "quest_new";
        [SerializeField] private string title = "새 퀘스트";
        [SerializeField] private QuestCategory category = QuestCategory.Side;

        [Header("내용")]
        [SerializeField, TextArea(2, 6)] private string summary = "";
        [SerializeField] private string questGiverId = "";
        [SerializeField, TextArea(2, 6)] private string startDialogue = "";
        [SerializeField, TextArea(2, 6)] private string completeDialogue = "";

        [Header("목표")]
        [SerializeField] private List<QuestObjective> objectives = new List<QuestObjective>();

        [Tooltip("켜면 목표를 다 채우는 즉시 완료 처리된다. 끄면 의뢰인에게 보고해야 한다.")]
        [SerializeField] private bool autoComplete;

        [Tooltip("켜면 완료 시 Collect 목표 아이템을 인벤토리에서 회수한다.")]
        [SerializeField] private bool consumeCollectedItems = true;

        [Header("보상")]
        [SerializeField] private QuestReward reward = new QuestReward();

        [Header("연결")]
        [Tooltip("이 퀘스트들을 모두 완료해야 수락할 수 있다.")]
        [SerializeField] private List<string> prerequisiteQuestIds = new List<string>();

        [Tooltip("완료 시 자동으로 열리는 다음 퀘스트.")]
        [SerializeField] private string nextQuestId = "";

        public string QuestId => string.IsNullOrEmpty(questId) ? name : questId;
        public string Title => title;
        public QuestCategory Category => category;
        public string Summary => summary;
        public string QuestGiverId => questGiverId;
        public string StartDialogue => startDialogue;
        public string CompleteDialogue => completeDialogue;

        public IReadOnlyList<QuestObjective> Objectives => objectives;
        public bool AutoComplete => autoComplete;
        public bool ConsumeCollectedItems => consumeCollectedItems;
        public QuestReward Reward => reward;
        public IReadOnlyList<string> PrerequisiteQuestIds => prerequisiteQuestIds;
        public string NextQuestId => nextQuestId;

        /// <summary>메인 퀘스트는 포기 불가.</summary>
        public bool CanAbandon => category != QuestCategory.Main;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(questId)) questId = name;
        }
#endif
    }
}
