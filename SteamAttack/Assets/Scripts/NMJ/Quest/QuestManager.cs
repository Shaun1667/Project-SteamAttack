using System;
using System.Collections.Generic;
using SteamAttack.InventorySystem;
using SteamAttack.Items;
using UnityEngine;

namespace SteamAttack.Quests
{
    /// <summary>
    /// 퀘스트 수락 / 진행 보고 / 완료와 보상 지급을 담당한다.
    /// Collect 목표는 인벤토리 보유량을 그대로 따라가고,
    /// Kill / Talk / Reach 목표는 외부에서 Report 로 알려준다.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        [Header("데이터")]
        [SerializeField] private QuestDatabase database;

        [Tooltip("게임 시작 시 자동으로 수락시킬 퀘스트(보통 메인 1장).")]
        [SerializeField] private QuestData startingQuest;

        [Tooltip("메인 퀘스트는 앞 단계를 끝내면 다음 단계를 자동 수락한다.")]
        [SerializeField] private bool autoAcceptMainQuests = true;

        private readonly Dictionary<string, QuestProgress> progresses = new Dictionary<string, QuestProgress>();
        private Inventory boundInventory;

        /// <summary>퀘스트 상태가 바뀔 때마다(수락/완료/잠금해제).</summary>
        public event Action<QuestProgress> QuestStatusChanged;
        /// <summary>목표 수치가 올라갈 때. 인자: 퀘스트, 목표 인덱스.</summary>
        public event Action<QuestProgress, int> ObjectiveProgressed;
        public event Action<QuestProgress> QuestAccepted;
        /// <summary>목표를 다 채워 보고만 남은 상태가 됐을 때.</summary>
        public event Action<QuestProgress> QuestReadyToComplete;
        public event Action<QuestProgress> QuestCompleted;
        /// <summary>경험치/재화 지급을 외부 시스템에 넘긴다. 인자: exp, gold.</summary>
        public event Action<int, int> RewardGranted;

        /// <summary>UI 가 추적 중인 퀘스트. 기본값은 진행 중인 메인 퀘스트.</summary>
        public QuestProgress TrackedQuest { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildInitialStates();
        }

        private void Start()
        {
            TryBindInventory();

            if (startingQuest != null) Accept(startingQuest.QuestId);
        }

        private void Update()
        {
            // PlayerInventory 가 QuestManager 보다 늦게 생겨도 붙도록 한 번 더 시도한다.
            if (boundInventory == null) TryBindInventory();
        }

        private void OnDestroy()
        {
            UnbindInventory();
            if (Instance == this) Instance = null;
        }

        // ── 조회 ───────────────────────────────────────────────

        public IEnumerable<QuestProgress> All => progresses.Values;

        public QuestProgress Get(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return progresses.TryGetValue(questId, out var p) ? p : null;
        }

        public QuestStatus GetStatus(string questId) => Get(questId)?.Status ?? QuestStatus.Locked;
        public bool IsCompleted(string questId) => GetStatus(questId) == QuestStatus.Completed;

        public List<QuestProgress> GetActiveQuests()
        {
            var list = new List<QuestProgress>();
            foreach (var p in progresses.Values)
                if (p.Status == QuestStatus.InProgress || p.Status == QuestStatus.ReadyToComplete)
                    list.Add(p);
            return list;
        }

        /// <summary>진행 중인 메인 퀘스트(없으면 null).</summary>
        public QuestProgress GetActiveMainQuest()
        {
            foreach (var p in progresses.Values)
            {
                if (p.Data.Category != QuestCategory.Main) continue;
                if (p.Status == QuestStatus.InProgress || p.Status == QuestStatus.ReadyToComplete) return p;
            }

            return null;
        }

        public void Track(string questId)
        {
            var progress = Get(questId);
            if (progress != null) TrackedQuest = progress;
        }

        // ── 수락 / 완료 ────────────────────────────────────────

        public bool CanAccept(string questId)
        {
            var progress = Get(questId);
            if (progress == null) return false;
            if (progress.Status != QuestStatus.Available && progress.Status != QuestStatus.Locked) return false;
            return ArePrerequisitesMet(progress.Data);
        }

        public bool Accept(string questId)
        {
            var progress = Get(questId);
            if (progress == null)
            {
                Debug.LogWarning($"[QuestManager] 등록되지 않은 퀘스트: {questId}");
                return false;
            }

            if (progress.Status == QuestStatus.InProgress || progress.Status == QuestStatus.ReadyToComplete) return false;
            if (progress.Status == QuestStatus.Completed && progress.Data.Category != QuestCategory.Repeatable) return false;
            if (!ArePrerequisitesMet(progress.Data)) return false;

            progress.Status = QuestStatus.InProgress;
            QuestAccepted?.Invoke(progress);
            QuestStatusChanged?.Invoke(progress);

            if (progress.Data.Category == QuestCategory.Main || TrackedQuest == null) TrackedQuest = progress;

            SyncCollectObjectives(progress);
            EvaluateCompletion(progress);
            return true;
        }

        /// <summary>목표를 다 채운 퀘스트를 완료 처리하고 보상을 준다.</summary>
        public bool Complete(string questId)
        {
            var progress = Get(questId);
            if (progress == null || progress.Status != QuestStatus.ReadyToComplete) return false;

            var inventory = PlayerInventory.Instance != null ? PlayerInventory.Instance.Inventory : null;
            var reward = progress.Data.Reward;

            // 보상 아이템을 받을 칸이 있는지 먼저 확인한다(칸 수 제한만 존재).
            if (inventory != null && !HasRoomForReward(inventory, progress.Data))
            {
                Debug.LogWarning($"[QuestManager] 인벤토리 칸이 모자라 '{progress.Data.Title}' 보상을 받을 수 없다.");
                return false;
            }

            if (progress.Data.ConsumeCollectedItems && inventory != null) ConsumeCollectedItems(progress, inventory);

            if (reward.ExtraInventorySlots > 0 && PlayerInventory.Instance != null)
                PlayerInventory.Instance.ExpandSlots(reward.ExtraInventorySlots);

            if (inventory != null)
            {
                foreach (var rewardItem in reward.Items)
                {
                    if (rewardItem.item == null) continue;
                    int leftover = inventory.Add(rewardItem.item, Mathf.Max(1, rewardItem.count));
                    if (leftover > 0)
                        Debug.LogWarning($"[QuestManager] 보상 {rewardItem.item.DisplayName} {leftover}개를 넣지 못했다(칸 부족).");
                }
            }

            progress.Status = QuestStatus.Completed;
            RewardGranted?.Invoke(reward.Exp, reward.Gold);
            QuestCompleted?.Invoke(progress);
            QuestStatusChanged?.Invoke(progress);

            UnlockFollowUps(progress.Data);

            if (TrackedQuest == progress) TrackedQuest = GetActiveMainQuest();
            return true;
        }

        public bool Abandon(string questId)
        {
            var progress = Get(questId);
            if (progress == null || !progress.Data.CanAbandon) return false;
            if (progress.Status != QuestStatus.InProgress && progress.Status != QuestStatus.ReadyToComplete) return false;

            progresses[questId] = new QuestProgress(progress.Data, QuestStatus.Available);
            QuestStatusChanged?.Invoke(progresses[questId]);
            if (TrackedQuest == progress) TrackedQuest = GetActiveMainQuest();
            return true;
        }

        // ── 진행 보고 ──────────────────────────────────────────

        public void ReportKill(string enemyId, int amount = 1) => Report(QuestObjectiveType.Kill, enemyId, amount);
        public void ReportTalk(string npcId) => Report(QuestObjectiveType.Talk, npcId, 1);
        public void ReportReach(string locationId) => Report(QuestObjectiveType.Reach, locationId, 1);
        public void ReportCustom(string customId, int amount = 1) => Report(QuestObjectiveType.Custom, customId, amount);

        public void Report(QuestObjectiveType type, string targetId, int amount = 1)
        {
            if (string.IsNullOrEmpty(targetId) || amount <= 0) return;

            foreach (var progress in progresses.Values)
            {
                if (progress.Status != QuestStatus.InProgress) continue;

                bool changed = false;
                var objectives = progress.Data.Objectives;
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (!objectives[i].Matches(type, targetId)) continue;
                    if (!progress.AddAmount(i, amount)) continue;

                    changed = true;
                    ObjectiveProgressed?.Invoke(progress, i);
                }

                if (changed) EvaluateCompletion(progress);
            }
        }

        // ── 내부 ───────────────────────────────────────────────

        private void BuildInitialStates()
        {
            progresses.Clear();
            if (database == null)
            {
                Debug.LogWarning("[QuestManager] QuestDatabase 가 비어 있다. 인스펙터에서 지정해라.");
                return;
            }

            foreach (var quest in database.Quests)
            {
                if (quest == null || progresses.ContainsKey(quest.QuestId)) continue;

                var status = quest.PrerequisiteQuestIds.Count == 0 ? QuestStatus.Available : QuestStatus.Locked;
                progresses[quest.QuestId] = new QuestProgress(quest, status);
            }
        }

        private bool ArePrerequisitesMet(QuestData data)
        {
            foreach (var id in data.PrerequisiteQuestIds)
                if (!IsCompleted(id)) return false;
            return true;
        }

        private void UnlockFollowUps(QuestData completed)
        {
            foreach (var progress in progresses.Values)
            {
                if (progress.Status != QuestStatus.Locked) continue;
                if (!ArePrerequisitesMet(progress.Data)) continue;

                progress.Status = QuestStatus.Available;
                QuestStatusChanged?.Invoke(progress);
            }

            if (string.IsNullOrEmpty(completed.NextQuestId)) return;

            var next = Get(completed.NextQuestId);
            if (next == null) return;

            bool shouldAutoAccept = autoAcceptMainQuests && next.Data.Category == QuestCategory.Main;
            if (shouldAutoAccept) Accept(next.Data.QuestId);
        }

        private void EvaluateCompletion(QuestProgress progress)
        {
            if (progress.Status != QuestStatus.InProgress) return;
            if (!progress.AreAllObjectivesComplete()) return;

            progress.Status = QuestStatus.ReadyToComplete;
            QuestReadyToComplete?.Invoke(progress);
            QuestStatusChanged?.Invoke(progress);

            if (progress.Data.AutoComplete) Complete(progress.Data.QuestId);
        }

        private bool HasRoomForReward(Inventory inventory, QuestData data)
        {
            int needed = data.Reward.EstimateRequiredSlots();
            if (needed <= 0) return true;

            // 회수될 퀘스트 아이템이 비워줄 칸까지는 계산하지 않는다(보수적으로 판단).
            return inventory.EmptySlotCount + data.Reward.ExtraInventorySlots >= needed;
        }

        private void ConsumeCollectedItems(QuestProgress progress, Inventory inventory)
        {
            var objectives = progress.Data.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                var objective = objectives[i];
                if (objective.Type != QuestObjectiveType.Collect || objective.TargetItem == null) continue;

                inventory.Remove(objective.TargetItem, objective.RequiredAmount);
            }
        }

        private void TryBindInventory()
        {
            if (PlayerInventory.Instance == null) return;

            boundInventory = PlayerInventory.Instance.Inventory;
            boundInventory.Changed += OnInventoryChanged;
            OnInventoryChanged();
        }

        private void UnbindInventory()
        {
            if (boundInventory == null) return;

            boundInventory.Changed -= OnInventoryChanged;
            boundInventory = null;
        }

        private void OnInventoryChanged()
        {
            foreach (var progress in progresses.Values)
            {
                if (progress.Status != QuestStatus.InProgress) continue;
                if (SyncCollectObjectives(progress)) EvaluateCompletion(progress);
            }
        }

        /// <summary>Collect 목표를 현재 보유량에 맞춘다. 값이 바뀌면 true.</summary>
        private bool SyncCollectObjectives(QuestProgress progress)
        {
            if (boundInventory == null) return false;

            bool changed = false;
            var objectives = progress.Data.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                var objective = objectives[i];
                if (objective.Type != QuestObjectiveType.Collect) continue;

                int owned = objective.TargetItem != null
                    ? boundInventory.CountOf(objective.TargetItem)
                    : boundInventory.CountOf(objective.TargetId);

                if (!progress.SetAmount(i, owned)) continue;

                changed = true;
                ObjectiveProgressed?.Invoke(progress, i);
            }

            return changed;
        }
    }
}
