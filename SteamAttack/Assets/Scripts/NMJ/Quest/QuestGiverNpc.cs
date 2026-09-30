using System.Collections.Generic;
using UnityEngine;

namespace SteamAttack.Quests
{
    /// <summary>
    /// 퀘스트를 주고 받는 NPC. 상호작용할 때 <see cref="Interact"/> 를 부르면
    /// 1) Talk 목표 보고 → 2) 완료 가능한 퀘스트 보고 처리 → 3) 새 퀘스트 제안 순으로 동작한다.
    /// </summary>
    public class QuestGiverNpc : MonoBehaviour
    {
        [Tooltip("Talk 목표의 targetId. 예: npc_gojong")]
        [SerializeField] private string npcId = "";

        [SerializeField] private string displayName = "";

        [Tooltip("이 NPC 가 줄 수 있는 퀘스트들.")]
        [SerializeField] private List<QuestData> offeredQuests = new List<QuestData>();

        [Tooltip("켜면 완료 가능한 퀘스트를 대화만으로 바로 보고 처리한다.")]
        [SerializeField] private bool autoTurnIn = true;

        public string NpcId => npcId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? npcId : displayName;

        /// <summary>플레이어 상호작용 진입점. 마지막에 보여줄 대사를 돌려준다.</summary>
        public string Interact()
        {
            var manager = QuestManager.Instance;
            if (manager == null) return string.Empty;

            if (!string.IsNullOrEmpty(npcId)) manager.ReportTalk(npcId);

            // 보고 대기 중인 퀘스트 먼저 처리
            foreach (var quest in offeredQuests)
            {
                if (quest == null) continue;
                if (manager.GetStatus(quest.QuestId) != QuestStatus.ReadyToComplete) continue;

                if (autoTurnIn && manager.Complete(quest.QuestId)) return quest.CompleteDialogue;
                return quest.CompleteDialogue;
            }

            // 새로 줄 수 있는 퀘스트
            foreach (var quest in offeredQuests)
            {
                if (quest == null || !manager.CanAccept(quest.QuestId)) continue;
                if (manager.Accept(quest.QuestId)) return quest.StartDialogue;
            }

            return string.Empty;
        }
    }
}
