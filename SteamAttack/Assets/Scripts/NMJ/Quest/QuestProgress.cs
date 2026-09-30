using System;
using UnityEngine;

namespace SteamAttack.Quests
{
    /// <summary>퀘스트 하나의 실행 중 상태(목표별 달성 수치 + 진행 단계).</summary>
    [Serializable]
    public class QuestProgress
    {
        [SerializeField] private QuestData data;
        [SerializeField] private int[] amounts;
        [SerializeField] private QuestStatus status;

        public QuestProgress(QuestData data, QuestStatus status = QuestStatus.Available)
        {
            this.data = data;
            this.status = status;
            amounts = new int[data != null ? data.Objectives.Count : 0];
        }

        public QuestData Data => data;
        public QuestStatus Status
        {
            get => status;
            internal set => status = value;
        }

        public int ObjectiveCount => data != null ? data.Objectives.Count : 0;

        public int GetAmount(int index)
        {
            return amounts != null && index >= 0 && index < amounts.Length ? amounts[index] : 0;
        }

        public int GetRequired(int index)
        {
            return index >= 0 && index < ObjectiveCount ? data.Objectives[index].RequiredAmount : 0;
        }

        public bool IsObjectiveComplete(int index) => GetAmount(index) >= GetRequired(index);

        public bool AreAllObjectivesComplete()
        {
            for (int i = 0; i < ObjectiveCount; i++)
                if (!IsObjectiveComplete(i)) return false;
            return true;
        }

        /// <summary>0~1 진행률.</summary>
        public float Ratio
        {
            get
            {
                if (ObjectiveCount == 0) return 1f;

                float sum = 0f;
                for (int i = 0; i < ObjectiveCount; i++)
                    sum += Mathf.Clamp01(GetAmount(i) / (float)Mathf.Max(1, GetRequired(i)));

                return sum / ObjectiveCount;
            }
        }

        /// <summary>목표 수치를 더한다. 실제로 값이 바뀌면 true.</summary>
        internal bool AddAmount(int index, int delta)
        {
            if (amounts == null || index < 0 || index >= amounts.Length || delta == 0) return false;

            int before = amounts[index];
            amounts[index] = Mathf.Clamp(before + delta, 0, GetRequired(index));
            return amounts[index] != before;
        }

        /// <summary>목표 수치를 특정 값으로 맞춘다(보유 개수 기반 Collect 목표용).</summary>
        internal bool SetAmount(int index, int value)
        {
            if (amounts == null || index < 0 || index >= amounts.Length) return false;

            int clamped = Mathf.Clamp(value, 0, GetRequired(index));
            if (amounts[index] == clamped) return false;

            amounts[index] = clamped;
            return true;
        }

        public string GetObjectiveLine(int index)
        {
            if (index < 0 || index >= ObjectiveCount) return string.Empty;

            var objective = data.Objectives[index];
            string mark = IsObjectiveComplete(index) ? "■" : "□";
            return $"{mark} {objective.GetDescription()} ({GetAmount(index)}/{objective.RequiredAmount})";
        }
    }
}
