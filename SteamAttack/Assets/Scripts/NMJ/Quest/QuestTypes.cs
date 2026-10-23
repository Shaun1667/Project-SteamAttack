using System;
using UnityEngine;

namespace NMJ
{
    /// <summary>퀘스트 분류. 메인 퀘스트는 포기할 수 없다.</summary>
    public enum QuestCategory
    {
        Main = 0,
        Side = 1,
        Repeatable = 2,
    }

    public enum QuestStatus
    {
        /// <summary>선행 퀘스트를 아직 못 깬 상태.</summary>
        Locked = 0,
        /// <summary>수락 가능.</summary>
        Available = 1,
        /// <summary>진행 중.</summary>
        InProgress = 2,
        /// <summary>목표는 다 채웠고 보고만 남은 상태.</summary>
        ReadyToComplete = 3,
        /// <summary>보상까지 받은 상태.</summary>
        Completed = 4,
    }

    public enum QuestObjectiveType
    {
        /// <summary>특정 NPC 와 대화 (targetId = NPC 아이디)</summary>
        Talk = 0,
        /// <summary>특정 적 처치 (targetId = 적 아이디)</summary>
        Kill = 1,
        /// <summary>(쓰지 않음 — 인벤토리 삭제. 저장된 값과 번호를 맞추려고 자리만 남김)</summary>
        Collect = 2,
        /// <summary>특정 지역 도달 (targetId = 지역 아이디)</summary>
        Reach = 3,
        /// <summary>그 외 스크립트에서 직접 보고하는 목표</summary>
        Custom = 4,
    }

    /// <summary>퀘스트 목표 한 줄.</summary>
    [Serializable]
    public class QuestObjective
    {
        [SerializeField] private QuestObjectiveType type = QuestObjectiveType.Kill;

        [Tooltip("적/NPC/지역/커스텀 아이디.")]
        [SerializeField] private string targetId = "";

        [Tooltip("UI 에 보일 문구. 비우면 자동 생성된다.")]
        [SerializeField] private string description = "";

        [SerializeField, Min(1)] private int requiredAmount = 1;

        public QuestObjectiveType Type => type;
        public int RequiredAmount => Mathf.Max(1, requiredAmount);

        /// <summary>판정에 쓰는 실제 키.</summary>
        public string TargetId => targetId;

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(description)) return description;

            string targetName = targetId;
            switch (type)
            {
                case QuestObjectiveType.Talk: return $"{targetName} 와(과) 대화";
                case QuestObjectiveType.Kill: return $"{targetName} 처치";
                case QuestObjectiveType.Reach: return $"{targetName} 도달";
                default: return targetName;
            }
        }

        public bool Matches(QuestObjectiveType objectiveType, string id)
        {
            return type == objectiveType
                   && !string.IsNullOrEmpty(id)
                   && string.Equals(TargetId, id, StringComparison.Ordinal);
        }
    }

    /// <summary>퀘스트 보상 (경험치·재화는 QuestManager.RewardGranted 이벤트로 넘긴다. 아이템 보상은 인벤토리와 함께 삭제)</summary>
    [Serializable]
    public class QuestReward
    {
        [SerializeField, Min(0)] private int exp;
        [SerializeField, Min(0)] private int gold;

        public int Exp => exp;
        public int Gold => gold;
    }
}
