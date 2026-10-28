using System;
using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 퀘스트 수락 / 진행 보고 / 완료와 보상(경험치·재화 이벤트) 지급을 담당한다.
    /// Kill / Talk / Reach / Custom 목표는 외부에서 Report 로 알려준다.
    /// (인벤토리·아이템은 시연 범위에서 빠져 2026-10-23 삭제 — Collect 목표·아이템 보상 없음)
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

        // 이 씬에서 지금까지 보고된 일 (처치·도착·대화·커스텀). 나중에 시작한 퀘스트도 이미 일어난 만큼 채운다
        // — 예: 육조 궁수를 두고 광화문에서 먼저 싸우면 2단계가 시작되기 전 처치가 사라져 퀘스트가 멈추던 문제
        private readonly List<(QuestObjectiveType type, string id, int amount)> reportHistory = new List<(QuestObjectiveType, string, int)>();

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
        // 런타임 전용. 직렬화되면 재컴파일 후 Data가 비어 있는 QuestProgress가 생겨 UI에서 NullReference가 난다. (NGH 수정)
        [field: NonSerialized] public QuestProgress TrackedQuest { get; private set; }

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
            if (startingQuest != null) Accept(startingQuest.QuestId);
        }

        private void OnDestroy()
        {
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

            ApplyEarlierReports(progress);
            EvaluateCompletion(progress);
            return true;
        }

        /// <summary>퀘스트를 받기 전에 이미 일어난 일을 목표에 채운다 (반복 퀘스트는 두 번 세지 않게 제외)</summary>
        private void ApplyEarlierReports(QuestProgress progress)
        {
            if (progress.Data.Category == QuestCategory.Repeatable || reportHistory.Count == 0) return;
            var objectives = progress.Data.Objectives;
            for (int i = 0; i < objectives.Count; i++)
                foreach (var r in reportHistory)
                    if (objectives[i].Matches(r.type, r.id) && progress.AddAmount(i, r.amount))
                        ObjectiveProgressed?.Invoke(progress, i);
        }

        /// <summary>목표를 다 채운 퀘스트를 완료 처리하고 보상을 준다.</summary>
        public bool Complete(string questId)
        {
            var progress = Get(questId);
            if (progress == null || progress.Status != QuestStatus.ReadyToComplete) return false;

            var reward = progress.Data.Reward;

            progress.Status = QuestStatus.Completed;
            NGH.ReviewedAudio.PlayUi("Initial/11_UI/QuestComplete_v03");
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
            if (type != QuestObjectiveType.Collect) reportHistory.Add((type, targetId, amount));

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
            reportHistory.Clear();
            TrackedQuest = null;
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
    }
}
