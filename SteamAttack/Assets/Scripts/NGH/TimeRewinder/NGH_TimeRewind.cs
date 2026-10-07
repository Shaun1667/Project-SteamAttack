using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 시간 역행 (NGH)
/// - 최근 N초(기본 5초) 동안 플레이어의 위치·회전, 체력, 증기 게이지, 애니메이션, 발도/납도·무기 상태를 계속 기록한다.
/// - N초보다 오래된 기록은 폐기한다.
/// - 발동하면 연출(RewindEffectRoutine, 지금은 비어 있음)을 먼저 재생한 뒤 N초 전 상태로 모두 되돌린다.
/// - 연출이 재생되는 동안 플레이어는 무적이다.
/// 플레이어 루트(NGH_PlayerController가 있는 오브젝트)에 붙인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NGH_PlayerController))]
public class NGH_TimeRewind : MonoBehaviour
{
    /// <summary>한 시점의 기록</summary>
    public class Snapshot
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
        public NGH_PlayerController.RewindState player;
        public bool hasSteam;
        public float steamPressure;

        // 카메라 (마우스로 돌린 화면 방향)
        public bool hasCamera;
        public float cameraYaw;
        public float cameraPitch;

        // 애니메이션
        public float animatorSpeed;
        public int[] stateHashes;
        public float[] stateTimes;
        public bool[] stateLoops;
        public float[] floatParams;
        public int[] intParams;
        public bool[] boolParams;
    }

    [Header("참조 (비워 두면 자동으로 찾음)")]
    [SerializeField] private NGH_PlayerController player;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Animator animator;
    [Tooltip("플레이어 등에 붙은 스팀백의 탱크 (비우면 자식에서 찾음)")]
    [SerializeField] private YPH_SteamTank steamTank;
    [Tooltip("플레이어를 따라가는 3인칭 카메라 (비우면 씬에서 찾음)")]
    [SerializeField] private NGH_ThirdPersonCamera playerCamera;

    [Header("기록")]
    [Tooltip("되돌아갈 시간(초). 이보다 오래된 기록은 폐기한다")]
    [SerializeField, Min(0.1f)] private float rewindSeconds = 5f;
    [Tooltip("기록 간격(초). 0이면 매 프레임 기록")]
    [SerializeField, Min(0f)] private float recordInterval = 0.02f;

    [Header("발동")]
    [Tooltip("시간 역행 키")]
    [SerializeField] private Key rewindKey = Key.R;
    [Tooltip("연출 중에는 플레이어 조작(이동·공격 입력)을 막는다")]
    [SerializeField] private bool lockControlDuringEffect = true;
    [Tooltip("사망 상태에서도 사용할 수 있게 한다")]
    [SerializeField] private bool allowWhileDead = false;

    [Header("사용 제한 (체크하면 해당 동작 중에는 사용할 수 없음)")]
    [Tooltip("공격(약·강공격, 2타, 사격) 중")]
    [SerializeField] private bool blockWhileAttacking = true;
    [Tooltip("발도·납도 동작 중")]
    [SerializeField] private bool blockWhileDrawSheath = true;
    [Tooltip("구르기 중")]
    [SerializeField] private bool blockWhileRolling = true;
    [Tooltip("피격(소·대) 동작 중")]
    [SerializeField] private bool blockWhileHit = true;

    [Header("Debug")]
    [Tooltip("화면 왼쪽 위에 기록 상태를 표시")]
    [SerializeField] private bool showDebugGUI = true;
    [SerializeField] private bool logRewind = true;

    /// <summary>시간 역행(연출 + 복원) 진행 중</summary>
    public bool IsRewinding { get; private set; }
    /// <summary>연출 중 무적</summary>
    public bool IsInvincible => IsRewinding;
    public float RewindSeconds => rewindSeconds;
    public int RecordCount => count;
    /// <summary>지금 되돌아갈 수 있는 시간(초). 기록이 쌓이는 중이면 rewindSeconds보다 짧다</summary>
    public float RecordedSeconds => count > 0 ? Time.time - buffer[head].time : 0f;

    /// <summary>시간 역행 시작 (연출 시작 직전)</summary>
    public event Action RewindStarted;
    /// <summary>시간 역행 끝 (복원 완료 직후)</summary>
    public event Action RewindFinished;

    // 오래된 기록부터 순서대로 쌓는 원형 버퍼. 스냅샷 객체는 재사용한다.
    private Snapshot[] buffer = new Snapshot[0];
    private int head;   // 가장 오래된 기록 위치
    private int count;
    private float lastRecordTime = float.NegativeInfinity;
    private Coroutine rewindRoutine;
    private bool controlLocked;

    // 애니메이터 파라미터 캐시 (Trigger는 기록하지 않음)
    private int layerCount;
    private int[] floatParamIds = new int[0];
    private int[] intParamIds = new int[0];
    private int[] boolParamIds = new int[0];

    #region Unity

    private void Awake()
    {
        if (!player) player = GetComponent<NGH_PlayerController>();
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (!animator) animator = player && player.animator ? player.animator : GetComponentInChildren<Animator>();
        if (!steamTank) steamTank = GetComponentInChildren<YPH_SteamTank>();

        CacheAnimatorLayout();
        EnsureCapacity(EstimateCapacity());
    }

    private void Start()
    {
        if (!playerCamera) playerCamera = FindPlayerCamera();
    }

    private void OnDisable()
    {
        // 연출 도중 꺼지면 무적·조작 잠금이 남지 않게 정리
        if (rewindRoutine != null)
        {
            StopCoroutine(rewindRoutine);
            rewindRoutine = null;
        }
        EndRewindLocks();
        IsRewinding = false;
    }

    private void Update()
    {
        if (IsRewinding) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || NGH_ControlsOverlay.IsOpen) return;

        if (keyboard[rewindKey].wasPressedThisFrame)
        {
            TryRewind();
        }
    }

    // 애니메이션이 갱신된 뒤에 기록한다
    private void LateUpdate()
    {
        if (IsRewinding || Time.deltaTime <= 0f) return;

        if (Time.time - lastRecordTime >= recordInterval - 0.0001f)
        {
            Record();
        }
        DiscardOld();
    }

    #endregion

    #region Public API

    /// <summary>시간 역행 발동. 이미 진행 중이거나 기록이 없으면 false</summary>
    public bool TryRewind()
    {
        if (IsRewinding || count == 0 || player == null) return false;
        if (player.IsDead && !allowWhileDead) return false;
        string blockedBy = GetBlockingAction();
        if (blockedBy != null)
        {
            if (logRewind) Debug.Log($"[CHG_TimeRewind] {blockedBy} 중에는 시간 역행을 사용할 수 없습니다.", this);
            return false;
        }

        rewindRoutine = StartCoroutine(RewindRoutine());
        return true;
    }

    /// <summary>공격 동작(약·강공격, 2타, 사격) 중인지</summary>
    public bool IsAttacking()
    {
        return player != null && IsAttackAction(player.CurrentAction);
    }

    /// <summary>
    /// 지금 시간 역행을 막는 동작 이름. 막지 않으면 null.
    /// Inspector의 "사용 제한" 체크 상태를 따른다.
    /// </summary>
    public string GetBlockingAction()
    {
        if (player == null) return null;
        NGH_PlayerController.ActionState a = player.CurrentAction;
        if (blockWhileAttacking && IsAttackAction(a)) return "공격";
        if (blockWhileDrawSheath && a == NGH_PlayerController.ActionState.Draw) return "발도";
        if (blockWhileDrawSheath && a == NGH_PlayerController.ActionState.Sheath) return "납도";
        if (blockWhileRolling && a == NGH_PlayerController.ActionState.Roll) return "구르기";
        if (blockWhileHit && (a == NGH_PlayerController.ActionState.HitSmall || a == NGH_PlayerController.ActionState.HitLarge)) return "피격";
        return null;
    }

    // 복원할 때 이어서 재생하지 않고 대기 상태로 바꿀 동작 (공격, 구르기, 발도/납도, 피격)
    private static bool ShouldDropOnRestore(NGH_PlayerController.ActionState a)
    {
        switch (a)
        {
            case NGH_PlayerController.ActionState.Roll:
            case NGH_PlayerController.ActionState.Draw:
            case NGH_PlayerController.ActionState.Sheath:
            case NGH_PlayerController.ActionState.HitSmall:
            case NGH_PlayerController.ActionState.HitLarge:
                return true;
            default:
                return IsAttackAction(a);
        }
    }

    private static bool IsAttackAction(NGH_PlayerController.ActionState a)
    {
        switch (a)
        {
            case NGH_PlayerController.ActionState.LightAttack:
            case NGH_PlayerController.ActionState.HeavyAttack:
            case NGH_PlayerController.ActionState.LightCombo2:
            case NGH_PlayerController.ActionState.LightCombo3:
            case NGH_PlayerController.ActionState.LightCombo4:
            case NGH_PlayerController.ActionState.LightCombo5:
            case NGH_PlayerController.ActionState.Shot:
                return true;
            default:
                return false;
        }
    }

    /// <summary>기록을 모두 지운다</summary>
    public void ClearHistory()
    {
        head = 0;
        count = 0;
        lastRecordTime = float.NegativeInfinity;
    }

    #endregion

    #region Rewind

    private IEnumerator RewindRoutine()
    {
        IsRewinding = true;

        // 되돌아갈 시점 = 남아 있는 가장 오래된 기록 (약 N초 전). 연출 중에는 기록하지 않으므로 그대로 유지된다.
        Snapshot target = buffer[head];
        float rewoundSeconds = Time.time - target.time;

        BeginRewindLocks();
        RewindStarted?.Invoke();

        // 1) 연출 (무적)
        yield return RewindEffectRoutine(target);

        // 2) N초 전 상태로 복원
        ApplySnapshot(target);

        // 되돌아간 이후의 기록은 존재하지 않는 미래가 되므로 모두 버리고 새로 쌓는다
        ClearHistory();
        Record();

        EndRewindLocks();
        IsRewinding = false;
        rewindRoutine = null;

        if (logRewind)
        {
            Debug.Log($"[CHG_TimeRewind] {rewoundSeconds:0.00}초 전으로 되돌림 → HP {player.Hp}, 증기 {(steamTank ? steamTank.CurrentPressure.ToString("0.#") : "-")}, {(player.IsDrawn ? "발도" : "납도")}", this);
        }
        RewindFinished?.Invoke();
    }

    /// <summary>
    /// 시간 역행 연출. 이 코루틴이 끝나면 N초 전 상태로 되돌아간다.
    /// 이 코루틴이 도는 동안 플레이어는 무적이다.
    /// </summary>
    /// <param name="target">되돌아갈 시점의 기록 (위치 등을 연출에 활용 가능)</param>
    protected virtual IEnumerator RewindEffectRoutine(Snapshot target)
    {
        // TODO: 시간 역행 연출을 여기에 작성 (화면 효과, 사운드, 잔상, 되감기 이동 등)
        yield break;
    }

    private void BeginRewindLocks()
    {
        player.SetExternalInvincible(this, true);
        if (lockControlDuringEffect && player.enabled)
        {
            player.enabled = false;
            controlLocked = true;
        }
    }

    private void EndRewindLocks()
    {
        if (player == null) return;
        player.SetExternalInvincible(this, false);
        if (controlLocked)
        {
            player.enabled = true;
            controlLocked = false;
        }
    }

    #endregion

    #region Record / Apply

    private void Record()
    {
        Snapshot s = PushSlot();
        s.time = Time.time;
        s.position = transform.position;
        s.rotation = transform.rotation;
        s.player = player.CaptureRewindState();

        s.hasSteam = steamTank != null;
        s.steamPressure = s.hasSteam ? steamTank.CurrentPressure : 0f;

        s.hasCamera = playerCamera != null;
        if (s.hasCamera)
        {
            Vector2 angles = playerCamera.ViewAngles;
            s.cameraYaw = angles.x;
            s.cameraPitch = angles.y;
        }

        if (AnimatorUsable())
        {
            s.animatorSpeed = animator.speed;
            for (int i = 0; i < layerCount; i++)
            {
                // 전환 중이면 넘어가는 대상 상태를 기록
                AnimatorStateInfo info = animator.IsInTransition(i) ? animator.GetNextAnimatorStateInfo(i) : animator.GetCurrentAnimatorStateInfo(i);
                s.stateHashes[i] = info.fullPathHash;
                s.stateTimes[i] = info.normalizedTime;
                s.stateLoops[i] = info.loop;
            }
            for (int i = 0; i < floatParamIds.Length; i++) s.floatParams[i] = animator.GetFloat(floatParamIds[i]);
            for (int i = 0; i < intParamIds.Length; i++) s.intParams[i] = animator.GetInteger(intParamIds[i]);
            for (int i = 0; i < boolParamIds.Length; i++) s.boolParams[i] = animator.GetBool(boolParamIds[i]);
        }

        lastRecordTime = Time.time;
    }

    private void ApplySnapshot(Snapshot s)
    {
        // 위치·회전 (CharacterController는 꺼야 순간이동이 적용된다)
        bool ccEnabled = characterController && characterController.enabled;
        if (ccEnabled) characterController.enabled = false;
        transform.SetPositionAndRotation(s.position, s.rotation);
        if (ccEnabled) characterController.enabled = true;

        // 체력, 행동, 발도/납도, 무기 칸, 락온
        // 공격·구르기·발도/납도·피격 동작이나 예약된 입력(버퍼)까지 되살리면 되돌아가자마자 그 동작이 이어서 나가므로,
        // 그런 동작 중이던 기록은 대기(이동) 상태로 바꾸고 예약 입력은 항상 비운다.
        NGH_PlayerController.RewindState ps = s.player;
        bool droppedAction = ShouldDropOnRestore(ps.action);
        if (droppedAction)
        {
            ps.action = NGH_PlayerController.ActionState.None;
            ps.actionTime = 0f;
            ps.actionDuration = 0f;
            ps.actionPlaySpeed = 1f;
            ps.hitWindowIndex = -1;
            ps.visualSwitched = false;
            ps.healedThisAction = false;
            ps.locoState = player.locomotionState;
        }
        ps.buffered = NGH_PlayerController.ActionState.None;
        player.RestoreRewindState(ps);

        // 증기 게이지
        if (steamTank && s.hasSteam)
        {
            steamTank.RestorePressure(s.steamPressure);
        }

        // 카메라 방향 (위치는 카메라가 다음 LateUpdate에서 플레이어 기준으로 다시 계산)
        if (playerCamera && s.hasCamera)
        {
            playerCamera.SetViewAngles(s.cameraYaw, s.cameraPitch);
        }

        // 애니메이션
        if (AnimatorUsable())
        {
            for (int i = 0; i < floatParamIds.Length; i++) animator.SetFloat(floatParamIds[i], s.floatParams[i]);
            for (int i = 0; i < intParamIds.Length; i++) animator.SetInteger(intParamIds[i], s.intParams[i]);
            for (int i = 0; i < boolParamIds.Length; i++) animator.SetBool(boolParamIds[i], s.boolParams[i]);
            animator.speed = s.animatorSpeed;
            for (int i = 0; i < layerCount; i++)
            {
                if (s.stateHashes[i] == 0) continue;
                float t = s.stateLoops[i] ? Mathf.Repeat(s.stateTimes[i], 1f) : Mathf.Clamp01(s.stateTimes[i]);
                animator.Play(s.stateHashes[i], i, t);
            }
            if (droppedAction)
            {
                // 진행 중이던 동작 대신 기본 이동 상태로
                animator.speed = 1f;
                animator.Play(player.locomotionState, 0, 0f);
            }
            animator.Update(0f);
        }

        Physics.SyncTransforms();
    }

    private NGH_ThirdPersonCamera FindPlayerCamera()
    {
        NGH_ThirdPersonCamera fallback = null;
        foreach (NGH_ThirdPersonCamera cam in FindObjectsByType<NGH_ThirdPersonCamera>(FindObjectsInactive.Exclude))
        {
            if (cam.target == transform) return cam;
            if (fallback == null) fallback = cam;
        }
        return fallback;
    }

    private bool AnimatorUsable()
    {
        return animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController && layerCount > 0;
    }

    private void CacheAnimatorLayout()
    {
        layerCount = 0;
        if (!animator || !animator.runtimeAnimatorController) return;

        layerCount = animator.layerCount;
        AnimatorControllerParameter[] parameters = animator.parameters;
        int f = 0, n = 0, b = 0;
        foreach (AnimatorControllerParameter p in parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float) f++;
            else if (p.type == AnimatorControllerParameterType.Int) n++;
            else if (p.type == AnimatorControllerParameterType.Bool) b++;
        }
        floatParamIds = new int[f];
        intParamIds = new int[n];
        boolParamIds = new int[b];
        f = n = b = 0;
        foreach (AnimatorControllerParameter p in parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float) floatParamIds[f++] = p.nameHash;
            else if (p.type == AnimatorControllerParameterType.Int) intParamIds[n++] = p.nameHash;
            else if (p.type == AnimatorControllerParameterType.Bool) boolParamIds[b++] = p.nameHash;
        }
    }

    #endregion

    #region Buffer

    private int EstimateCapacity()
    {
        float interval = Mathf.Max(recordInterval, 1f / 120f);
        return Mathf.CeilToInt(rewindSeconds / interval) + 8;
    }

    private Snapshot CreateSnapshot()
    {
        return new Snapshot
        {
            stateHashes = new int[layerCount],
            stateTimes = new float[layerCount],
            stateLoops = new bool[layerCount],
            floatParams = new float[floatParamIds.Length],
            intParams = new int[intParamIds.Length],
            boolParams = new bool[boolParamIds.Length],
        };
    }

    private void EnsureCapacity(int capacity)
    {
        if (buffer.Length >= capacity) return;

        Snapshot[] next = new Snapshot[capacity];
        for (int i = 0; i < count; i++)
        {
            next[i] = buffer[(head + i) % buffer.Length];
        }
        buffer = next;
        head = 0;
    }

    private Snapshot PushSlot()
    {
        if (count == buffer.Length)
        {
            EnsureCapacity(Mathf.Max(16, buffer.Length * 2));
        }
        int index = (head + count) % buffer.Length;
        if (buffer[index] == null)
        {
            buffer[index] = CreateSnapshot();
        }
        count++;
        return buffer[index];
    }

    // rewindSeconds보다 오래된 기록 폐기
    private void DiscardOld()
    {
        float limit = Time.time - rewindSeconds;
        while (count > 1 && buffer[head].time < limit)
        {
            head = (head + 1) % buffer.Length;
            count--;
        }
    }

    #endregion

    #region Debug GUI

    private void OnGUI()
    {
        if (!showDebugGUI || player == null) return;

        string steam = steamTank ? $"{steamTank.CurrentPressure:0}/{steamTank.MaxPressure:0}" : "-";
        string state = IsRewinding ? "REWINDING (invincible)"
                     : GetBlockingAction() != null ? $"Blocked ({player.CurrentAction})"
                     : $"[{rewindKey}] Rewind";
        string text = $"Time Rewind  {state}\n" +
                      $"Recorded {RecordedSeconds:0.00}s / {rewindSeconds:0.#}s  ({count})\n" +
                      $"HP {player.Hp}/{player.maxHp}   Steam {steam}   {(player.IsDrawn ? "Drawn" : "Sheathed")}";
        GUI.Box(new Rect(10, 10, 300, 62), GUIContent.none);
        GUI.Label(new Rect(18, 14, 290, 58), text);
    }

    #endregion
}
