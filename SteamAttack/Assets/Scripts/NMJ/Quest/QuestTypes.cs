using System;
using SteamAttack.Items;
using UnityEngine;

namespace SteamAttack.Quests
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
        /// <summary>아이템 보유 (targetItem / targetId = 아이템 아이디)</summary>
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

        [Tooltip("적/NPC/지역 아이디. Collect 는 targetItem 을 쓰면 비워도 된다.")]
        [SerializeField] private string targetId = "";

        [Tooltip("Collect 목표에서 모아야 할 아이템.")]
        [SerializeField] private ItemData targetItem;

        [Tooltip("UI 에 보일 문구. 비우면 자동 생성된다.")]
        [SerializeField] private string description = "";

        [SerializeField, Min(1)] private int requiredAmount = 1;

        public QuestObjectiveType Type => type;
        public ItemData TargetItem => targetItem;
        public int RequiredAmount => Mathf.Max(1, requiredAmount);

        /// <summary>판정에 쓰는 실제 키.</summary>
        public string TargetId => targetItem != null ? targetItem.ItemId : targetId;

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(description)) return description;

            string targetName = targetItem != null ? targetItem.DisplayName : targetId;
            switch (type)
            {
                case QuestObjectiveType.Talk: return $"{targetName} 와(과) 대화";
                case QuestObjectiveType.Kill: return $"{targetName} 처치";
                case QuestObjectiveType.Collect: return $"{targetName} 수집";
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

    /// <summary>퀘스트 보상.</summary>
    [Serializable]
    public class QuestReward
    {
        [SerializeField] private RewardItem[] items;
        [SerializeField, Min(0)] private int exp;
        [SerializeField, Min(0)] private int gold;

        [Tooltip("보상으로 늘려줄 인벤토리 칸 수 (하중이 아니라 칸 수로만 성장한다).")]
        [SerializeField, Min(0)] private int extraInventorySlots;

        public RewardItem[] Items => items ?? Array.Empty<RewardItem>();
        public int Exp => exp;
        public int Gold => gold;
        public int ExtraInventorySlots => extraInventorySlots;

        /// <summary>보상 아이템이 차지할 최소 칸 수(대략치). 인벤토리 여유 확인용.</summary>
        public int EstimateRequiredSlots()
        {
            int slots = 0;
            foreach (var reward in Items)
            {
                if (reward.item == null) continue;
                slots += Mathf.CeilToInt(Mathf.Max(1, reward.count) / (float)reward.item.MaxStack);
            }

            return slots;
        }

        [Serializable]
        public struct RewardItem
        {
            public ItemData item;
            [Min(1)] public int count;
        }
    }
}
