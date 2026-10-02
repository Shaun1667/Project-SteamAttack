using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 3인칭 플레이어 이동 + 근접 전투 프로토타입.
///  WASD 8방향(카메라 기준, 대각선도 같은 속도) / Shift 달리기 / Space 구르기(무적)
///  F 발도·납도 / 좌클릭 약공격 / 우클릭 강공격 / 휠클릭 락온 / Tab 무기 교체
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class chg_PlayerController : MonoBehaviour
{
    public enum ActionState { None, Roll, LightAttack, HeavyAttack, Shot, Draw, Sheath, HitSmall, HitLarge, LightCombo2, Death, StandUp }

    [Header("참조")]
    public Animator animator;
    public Transform cameraTransform;
    public chg_WeaponHolder weapons;

    [Header("체력")]
    public int maxHp = 3;
    [SerializeField] int hp = 3;
    [Tooltip("적에게 공격이 맞으면 회복하는 양 (공격 동작 1회당 한 번)")]
    public int healOnHit = 1;
    [Tooltip("부활 동작이 끝난 뒤 추가 무적 시간(초). 부활 동작 중에도 무적")]
    public float respawnInvincibleTime = 1f;
    [Tooltip("사망 동작 상태 이름 (쓰러지는 동작)")]
    public string deathState = "Death";
    [Tooltip("사망 동작을 몇 % 지점에서 멈춰 둘지 (1 = 끝까지 재생 후 마지막 자세 유지)")]
    [Range(0, 1)] public float deathHoldAt = 1f;
    public float deathClipLength = 1.7f;
    [Tooltip("사망(쓰러짐) 동작 재생 속도")]
    public float deathPlaySpeed = 1f;
    [Tooltip("부활(일어남) 동작 상태 이름")]
    public string standupState = "StandUp";
    public float standupClipLength = 1f;
    [Tooltip("부활(일어남) 동작 재생 속도")]
    public float standupPlaySpeed = 1.5f;

    public int Hp => hp;
    public bool IsDead => action == ActionState.Death;
    /// <summary>쓰러지는 동작이 끝까지 재생됐는지 (Game Over 화면 표시 시점)</summary>
    public bool IsDeathAnimFinished => IsDead && _actionTime >= deathClipLength * deathHoldAt / Mathf.Max(0.01f, deathPlaySpeed);
    public bool IsStandingUp => action == ActionState.StandUp;
    public float DeadTime { get; private set; }
    /// <summary>(현재 HP, 최대 HP) — HP가 바뀔 때마다 호출</summary>
    public event System.Action<int, int> HpChanged;
    /// <summary>살아 있던 대상을 근접 공격으로 실제로 맞힌 뒤 한 번 알립니다. (병합 — YPH 증기 회복 연결용)</summary>
    public event System.Action<chg_Damageable> OnHitLanded;
    /// <summary>원거리 장비(isMelee = false) 사격 동작이 시작될 때 장착 무기와 함께 알립니다. 실제 발사·투척은 구독하는 쪽이 처리합니다. (병합 — YPH 총·수류탄 연결용)</summary>
    public event System.Action<chg_Weapon> ShotStarted;

    [Header("이동 속도 (m/s)")]
    public float walkSpeed = 0.7f;
    public float combatWalkSpeed = 0.6f;
    public float backpedalSpeed = 1.44f;
    [Tooltip("뒷걸음 애니메이션 재생 속도 (걷기 동작을 거꾸로 재생, 1 = 걷기와 같은 빠르기)")]
    public float backpedalAnimSpeed = 0.72f;
    public float strafeSpeed = 0.77f;
    [Tooltip("락온 중 좌/우 걷기 동작 재생 속도 (동작 원래 속도 약 1m/s 기준, Strafe Speed와 맞추면 발이 덜 미끄러짐)")]
    public float strafeAnimSpeed = 0.8f;
    public float runSpeed = 3.5f;
    public float turnSpeed = 720f;        // 도/초
    public float gravity = -20f;
    [Tooltip("일반 이동의 속도 배율입니다. 1이면 기존 속도, 0이면 제자리이며 구르기·공격 이동·중력에는 적용하지 않습니다. (병합 — YPH 조준 배율 연결용)")]
    public float MoveSpeedMultiplier = 1f;

    [System.Serializable]
    public class AttackData
    {
        [Tooltip("클립 원래 길이(초). 0이면 씬 구성 때 입력된 값을 사용")]
        public float clipLength;
        [Tooltip("재생 속도 배율 (1 = 원래 속도, 1.5 = 1.5배 빠르게)")]
        public float playSpeed = 1f;
        [Tooltip("클립의 몇 % 지점에서 동작을 끝내고 조작을 돌려줄지 (후딜레이 조절, 1 = 끝까지)")]
        [Range(0.05f, 1f)] public float endAt = 1f;
        [Tooltip("공격 판정 구간 (클립 기준 0~1). 2단베기는 구간 2개 = 2번 맞음")]
        public Vector2[] hitWindows = { new Vector2(0.25f, 0.6f) };
        [Tooltip("켜면 판정 구간이 여러 개여도 한 대상에게 이 동작 동안 한 번만 맞음 (모션당 데미지 1회)")]
        public bool hitOncePerAction;
        [Tooltip("판정 구간(휘두르는 순간)마다 앞으로 쭉 나가는 거리(m). 0 = 제자리")]
        public float lungeDistance = -1f;   // -1 = 기본값 사용

        public float ActionTime => clipLength * endAt / Mathf.Max(0.01f, playSpeed);
    }

    [Header("구르기")]
    public float rollDistance = 3f;
    [Tooltip("구르기 전체 시간(초)")]
    public float rollTime = 0.9f;
    [Tooltip("구르기 애니메이션 재생 속도 배율")]
    public float rollAnimSpeed = 1.6f;
    [Tooltip("구르기 무적 시간(초). 추후 조정")]
    public float rollInvincibleTime = 0.5f;

    [Header("공격 — 후딜레이는 End At, 빠르기는 Play Speed로 조절")]
    public AttackData lightAttack = new AttackData
        { playSpeed = 2f, endAt = 0.30f, hitWindows = new[] { new Vector2(0.13f, 0.27f) }, lungeDistance = 0.4f };
    public AttackData heavyAttack = new AttackData   // 파워슬래시(powerslash): 회전 점프 베기, 판정 1번
        { playSpeed = 2f, endAt = 0.57f, hitWindows = new[] { new Vector2(0.345f, 0.39f) }, lungeDistance = 0.5f };   // 내려치는 순간(프레임 52~58)
    [Tooltip("약공격 2타: 약공격 중 좌클릭을 한 번 더 누르면 이어지는 2단베기 동작 (데미지는 약공격 기준)")]
    public AttackData lightCombo2 = new AttackData
        { playSpeed = 2f, endAt = 0.66f, hitWindows = new[] { new Vector2(0.18f, 0.31f), new Vector2(0.48f, 0.61f) }, lungeDistance = 0.15f, hitOncePerAction = false };   // 두 번 베기 = 판정 2번
    [Tooltip("약공격이 이 비율(0~1) 이상 진행된 뒤 누른 좌클릭은 2타로 이어짐")]
    [Range(0, 1)] public float comboInputFrom = 0.2f;
    [Tooltip("판정 구간보다 이만큼(클립 기준 0~1) 먼저 전진을 시작")]
    [Range(0, 0.2f)] public float lungeLead = 0.04f;
    [Tooltip("락온 대상과 이 거리(m)보다 가까우면 더 파고들지 않음")]
    public float lungeStopDistance = 0.6f;
    public AttackData shotAttack = new AttackData
        { playSpeed = 1f, endAt = 0.45f, hitWindows = new Vector2[0] };
    [Header("발도 / 납도 (F) — 각각 Draw Sword / Sheath Sword 동작")]
    public AttackData drawAction = new AttackData { playSpeed = 1f, endAt = 1f, hitWindows = new Vector2[0] };
    public AttackData sheathAction = new AttackData { playSpeed = 1f, endAt = 1f, hitWindows = new Vector2[0] };
    [Tooltip("발도 동작의 몇 % 지점에서 칼이 손에 나타날지")]
    [Range(0, 1)] public float drawShowAt = 0.3f;
    [Tooltip("납도 동작의 몇 % 지점에서 칼이 사라질지")]
    [Range(0, 1)] public float sheathHideAt = 0.75f;
    [Tooltip("공격하거나 락온할 때 발도 동작 없이 즉시 칼을 꺼냄")]
    public bool instantDrawOnAttack = false;   // 끔: 납도 상태에서 공격하면 발도 동작 후 공격

    [Header("피격 — 동작이 재생되는 동안 무적")]
    [Tooltip("데미지 1: 피격(소)")]
    public AttackData hitSmallAction = new AttackData { playSpeed = 1f, endAt = 1f, hitWindows = new Vector2[0] };
    [Tooltip("데미지 2 이상: 피격(대)")]
    public AttackData hitLargeAction = new AttackData { playSpeed = 1f, endAt = 1f, hitWindows = new Vector2[0] };
    [Tooltip("테스트용: 숫자 1 = 데미지 1, 숫자 2 = 데미지 2 를 자신에게 줌")]
    public bool debugHitKeys = true;

    [HideInInspector] public float attackForwardRatio;   // 이전 버전 호환 (지금은 휘두를 때만 전진)
    [Tooltip("공격 중 이동 속도 = 전투 걷기 속도 × 이 값 (0.1 = 10%)")]
    [Range(0, 1)] public float attackMoveRatio = 0.1f;
    [Tooltip("동작이 이 비율만큼 남았을 때부터 다음 입력을 미리 받아둠")]
    [Range(0, 1)] public float bufferWindow = 0.35f;

    // 이전 버전 씬 호환용 (클립 길이)
    [HideInInspector] public float lightAttackDuration, heavyAttackDuration, shotDuration, rollDuration;

    [Header("락온")]
    [Tooltip("락온 가능 거리(m). 락온 후 이 거리의 1.3배보다 멀어지면 해제")]
    public float lockOnRange = 9f;

    [Header("애니메이터 상태 이름")]
    public string locomotionState = "Locomotion";
    public string rollState = "Roll";
    public string lightState = "LightAttack";
    public string heavyState = "HeavyAttack";
    public string combo2State = "Combo2";
    public string strafeLeftState = "StrafeLeft";
    public string strafeRightState = "StrafeRight";
    public string shotState = "Shot";
    public string drawState = "Draw";
    public string sheathState = "Sheath";
    public string hitSmallState = "HitSmall";
    public string hitLargeState = "HitLarge";

    [Header("현재 상태 (확인용)")]
    [SerializeField] bool drawn;
    [SerializeField] ActionState action;
    [SerializeField] chg_Damageable lockTarget;

    public bool IsDrawn => drawn;
    public bool IsHitReacting => action == ActionState.HitSmall || action == ActionState.HitLarge;
    /// <summary>구르기 무적 시간 중이거나 피격 동작 중이면 무적</summary>
    public bool IsInvincible => (action == ActionState.Roll && _actionTime < rollInvincibleTime) || IsHitReacting
                                || IsDead || Time.time < _spawnInvincibleUntil
                                || _externalInvincible.Count > 0;   // 외부 무적 (예: 시간 역행 연출, NGH_TimeRewind)
    public chg_Damageable LockTarget => lockTarget;
    public ActionState CurrentAction => action;

    CharacterController _cc;
    float _vy;
    float _actionTime, _actionDuration, _actionPlaySpeed = 1f;
    AttackData _attack;
    int _hitWindowIndex = -1;
    bool _visualSwitched;
    bool _healedThisAction;
    float _spawnInvincibleUntil;
    string _locoState = "Locomotion";
    Vector3 _rollDir;
    ActionState _buffered;
    readonly HashSet<chg_Damageable> _hitThisSwing = new HashSet<chg_Damageable>();
    readonly HashSet<object> _externalInvincible = new HashSet<object>();   // 외부에서 켠 무적 (켠 쪽별로 관리)

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AnimSpeedHash = Animator.StringToHash("AnimSpeed");

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        hp = maxHp;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!weapons) weapons = GetComponent<chg_WeaponHolder>();
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (lightAttack.clipLength <= 0f) lightAttack.clipLength = lightAttackDuration > 0f ? lightAttackDuration : 1f;
        if (heavyAttack.clipLength <= 0f) heavyAttack.clipLength = heavyAttackDuration > 0f ? heavyAttackDuration : 1f;
        if (lightAttack.lungeDistance < 0f) lightAttack.lungeDistance = 0.4f;
        if (heavyAttack.lungeDistance < 0f) heavyAttack.lungeDistance = 0.3f;
        if (lightCombo2.clipLength <= 0f) lightCombo2.clipLength = heavyAttackDuration > 0f ? heavyAttackDuration : heavyAttack.clipLength;
        if (lightCombo2.lungeDistance < 0f) lightCombo2.lungeDistance = 0.15f;
        if (shotAttack.clipLength <= 0f) shotAttack.clipLength = shotDuration > 0f ? shotDuration : 1f;
        if (drawAction.clipLength <= 0f) drawAction.clipLength = 0.5f;
        if (sheathAction.clipLength <= 0f) sheathAction.clipLength = drawAction.clipLength;
        if (hitSmallAction.clipLength <= 0f) hitSmallAction.clipLength = 1.6f;
        if (hitLargeAction.clipLength <= 0f) hitLargeAction.clipLength = 1.7f;
    }

    void Start()
    {
        if (weapons) weapons.SetDrawn(drawn);
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;
        if (chg_ControlsOverlay.IsOpen) return;   // ESC 조작키 안내가 열려 있으면 입력 무시
        if (IsDead) { UpdateDeath(); ApplyGravityOnly(); return; }

        // ---------- 입력
        Vector2 input = ReadMoveInput();
        bool run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

        if (debugHitKeys)
        {
            if (kb.digit1Key.wasPressedThisFrame) TakeHit(1, transform.position + transform.forward);
            if (kb.digit2Key.wasPressedThisFrame) TakeHit(2, transform.position + transform.forward);
        }
        if (kb.fKey.wasPressedThisFrame) Request(drawn ? ActionState.Sheath : ActionState.Draw);
        if (kb.tabKey.wasPressedThisFrame && weapons && action == ActionState.None) weapons.SwapNext();
        if (mouse.middleButton.wasPressedThisFrame) ToggleLockOn();

        if (kb.spaceKey.wasPressedThisFrame) Request(ActionState.Roll);
        if (mouse.leftButton.wasPressedThisFrame) Request(IsMeleeEquipped() ? ActionState.LightAttack : ActionState.Shot);
        if (mouse.rightButton.wasPressedThisFrame && IsMeleeEquipped()) Request(ActionState.HeavyAttack);

        ValidateLockOn();

        Vector3 moveDir = CameraRelative(input);

        // ---------- 행동 중 (구르기/공격)
        if (action != ActionState.None)
        {
            UpdateAction();
            // 공격 중에는 원래 속도의 일부(기본 10%)로만 자리 이동 (방향은 그대로)
            float baseSpeed = drawn ? combatWalkSpeed : walkSpeed;
            if (action != ActionState.Roll && action != ActionState.None && !IsHitReacting && !IsStandingUp && input.sqrMagnitude > 0.01f)
                _cc.Move(moveDir * baseSpeed * attackMoveRatio * Time.deltaTime);
            ApplyGravityOnly();
            return;
        }

        // ---------- 이동
        float speed = 0f, animSpeedParam = 0f, playRate = 1f;
        int strafeSide = 0;   // 락온 중 좌(-1) / 우(+1) 걷기 동작
        Vector3 face = Vector3.zero;
        bool hasInput = input.sqrMagnitude > 0.01f;

        if (hasInput)
        {
            if (run)
            {
                speed = runSpeed; animSpeedParam = 1f; face = moveDir;
            }
            else if (lockTarget)
            {
                // 락온: 대상을 바라보며 이동 (A/D 옆걸음, S 뒷걸음)
                face = Flat(lockTarget.transform.position - transform.position);
                float fwd = Vector3.Dot(moveDir, face.normalized);
                bool sideInput = input.y >= -0.1f && Mathf.Abs(input.x) > 0.1f;   // A, D, WA, WD
                if (sideInput)
                {
                    speed = strafeSpeed; strafeSide = input.x < 0f ? -1 : 1; playRate = strafeAnimSpeed;
                }
                else if (fwd < -0.3f) { speed = backpedalSpeed; playRate = -backpedalAnimSpeed; }
                else if (fwd > 0.3f) speed = combatWalkSpeed;
                else speed = strafeSpeed;
                animSpeedParam = 0.5f;
            }
            else if (drawn && input.y < -0.1f)
            {
                // 전투 상태 S / SA / SD: 이동 반대쪽을 보며 뒷걸음질
                speed = backpedalSpeed; face = -moveDir; playRate = -backpedalAnimSpeed; animSpeedParam = 0.5f;
            }
            else
            {
                // 대기 상태 전방향 / 전투 상태 W, WA, WD, A, D: 이동 방향을 보고 걷기
                speed = drawn ? combatWalkSpeed : walkSpeed; face = moveDir; animSpeedParam = 0.5f;
            }
        }
        else if (lockTarget)
        {
            face = Flat(lockTarget.transform.position - transform.position);
        }

        if (face.sqrMagnitude > 0.0001f) TurnTowards(face);

        Vector3 velocity = moveDir * (speed * MoveSpeedMultiplier);
        _vy = _cc.isGrounded ? -2f : _vy + gravity * Time.deltaTime;
        velocity.y = _vy;
        _cc.Move(velocity * Time.deltaTime);

        if (animator)
        {
            animator.SetFloat(SpeedHash, animSpeedParam, 0.1f, Time.deltaTime);
            animator.SetFloat(AnimSpeedHash, playRate);
            string want = strafeSide < 0 ? strafeLeftState : strafeSide > 0 ? strafeRightState : locomotionState;
            if (want != _locoState)
            {
                animator.CrossFadeInFixedTime(want, 0.15f);
                _locoState = want;
            }
        }
    }

    // ================================================================ 행동

    /// <summary>
    /// 플레이어가 맞았을 때 호출. 무적 중이면 무시하고 false 반환.
    /// 데미지 1 = 피격(소), 2 이상 = 피격(대). 피격 동작이 끝날 때까지 무적.
    /// </summary>
    public bool TakeHit(int damage, Vector3 from)
    {
        if (damage <= 0 || IsInvincible) return false;
        Vector3 to = Flat(from - transform.position);
        if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to);   // 때린 쪽을 봄
        _buffered = ActionState.None;
        SetHp(hp - damage);
        if (hp <= 0) { Die(); return true; }
        StartAction(damage >= 2 ? ActionState.HitLarge : ActionState.HitSmall);
        Debug.Log($"[CHG] 플레이어 피격 데미지 {damage} → {(damage >= 2 ? "피격(대)" : "피격(소)")}, HP {hp}/{maxHp}, {_actionDuration:0.##}초 무적");
        return true;
    }

    void SetHp(int value)
    {
        int v = Mathf.Clamp(value, 0, maxHp);
        if (v == hp) return;
        hp = v;
        HpChanged?.Invoke(hp, maxHp);
    }

    /// <summary>HP 회복 (최대 HP를 넘지 않음)</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0 || hp >= maxHp) return;
        SetHp(hp + amount);
        Debug.Log($"[CHG] 적중 회복 +{amount} → HP {hp}/{maxHp}");
    }

    void Die()
    {
        SetLockTarget(null);
        _attack = null;
        action = ActionState.Death;
        _actionTime = 0f;
        DeadTime = Time.time;
        if (animator)
        {
            animator.speed = deathPlaySpeed;
            animator.CrossFadeInFixedTime(deathState, 0.3f);
        }
        Debug.Log("[CHG] 플레이어 사망 → Game Over");
    }

    void UpdateDeath()
    {
        _actionTime += Time.deltaTime;
        // 쓰러진 자세에서 멈춰 둠
        if (animator && animator.speed > 0f && IsDeathAnimFinished) animator.speed = 0f;
    }

    /// <summary>그 자리에서 다시 시작 (Game Over 화면에서 호출). 일어나는 동작 + 1초 동안 무적</summary>
    public void Respawn()
    {
        _attack = null; _buffered = ActionState.None;
        SetHp(maxHp);
        action = ActionState.StandUp;
        _actionTime = 0f;
        _actionPlaySpeed = standupPlaySpeed;
        _actionDuration = standupClipLength / Mathf.Max(0.01f, standupPlaySpeed);
        _spawnInvincibleUntil = Time.time + _actionDuration + respawnInvincibleTime;
        if (animator)
        {
            animator.speed = standupPlaySpeed;
            animator.CrossFadeInFixedTime(standupState, 0.3f);
        }
        _locoState = locomotionState;
        Debug.Log("[CHG] 다시 시작 — HP " + hp + "/" + maxHp);
    }

    void Request(ActionState a)
    {
        if (IsHitReacting || IsDead || IsStandingUp) return;   // 피격·사망·부활 중에는 입력 무시
        if (action == ActionState.None) { StartAction(a); return; }
        // 약공격 중 좌클릭 → 2타(2단베기)를 예약
        if (a == ActionState.LightAttack && action == ActionState.LightAttack
            && _actionTime >= _actionDuration * comboInputFrom)
        {
            _buffered = ActionState.LightCombo2;
            return;
        }
        // 행동이 끝나갈 때 들어온 입력은 예약해 두었다가 바로 이어서 실행
        if (_actionTime >= _actionDuration * (1f - bufferWindow)) _buffered = a;
    }

    void StartAction(ActionState a)
    {
        bool isHit = a == ActionState.HitSmall || a == ActionState.HitLarge;
        if (!isHit && weapons && weapons.IsSwapping && a != ActionState.Roll) return;
        if (a == ActionState.Draw && drawn) return;
        if (a == ActionState.Sheath && !drawn) return;
        bool isAttack = a == ActionState.LightAttack || a == ActionState.HeavyAttack || a == ActionState.Shot
                        || a == ActionState.LightCombo2;
        if (isAttack && !drawn)
        {
            // 납도 상태에서 공격: 즉시 발도하거나, 발도 동작 뒤에 공격을 이어서 실행
            if (instantDrawOnAttack) SetDrawn(true);
            else { StartAction(ActionState.Draw); _buffered = a; return; }
        }
        if (a == ActionState.Sheath) SetLockTarget(null);
        _visualSwitched = false;

        Vector3 moveDir = CameraRelative(ReadMoveInput());
        action = a;
        _actionTime = 0f;
        _buffered = ActionState.None;
        _hitThisSwing.Clear();
        _hitWindowIndex = -1;
        _healedThisAction = false;
        _attack = null;

        string state = locomotionState;
        switch (a)
        {
            case ActionState.Roll:
                _rollDir = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : transform.forward;
                transform.rotation = Quaternion.LookRotation(_rollDir);
                _actionDuration = rollTime; _actionPlaySpeed = rollAnimSpeed; state = rollState; break;
            case ActionState.LightAttack: _attack = lightAttack; state = lightState; break;
            case ActionState.HeavyAttack: _attack = heavyAttack; state = heavyState; break;
            case ActionState.LightCombo2: _attack = lightCombo2; state = combo2State; break;   // 2단베기 동작
            case ActionState.Shot: _attack = shotAttack; state = shotState; ShotStarted?.Invoke(weapons ? weapons.Current : null); break;
            case ActionState.Draw: _attack = drawAction; state = drawState; break;
            case ActionState.Sheath: _attack = sheathAction; state = sheathState; break;
            case ActionState.HitSmall: _attack = hitSmallAction; state = hitSmallState; break;
            case ActionState.HitLarge: _attack = hitLargeAction; state = hitLargeState; break;
        }
        if (_attack != null)
        {
            _actionDuration = _attack.ActionTime;
            _actionPlaySpeed = _attack.playSpeed;
            if (isAttack) FaceAttackDirection(moveDir);
        }
        if (animator)
        {
            animator.SetFloat(AnimSpeedHash, 1f);
            animator.speed = _actionPlaySpeed;
            animator.CrossFadeInFixedTime(state, 0.08f);
        }
    }

    void UpdateAction()
    {
        _actionTime += Time.deltaTime;
        float t = _actionTime / Mathf.Max(0.01f, _actionDuration);

        if (action == ActionState.Roll)
        {
            // 처음엔 빠르게, 끝에 감속 (평균 속도 = 거리/시간)
            float speed = rollDistance / Mathf.Max(0.05f, rollTime) * Mathf.Lerp(1.4f, 0.6f, t);
            _cc.Move(_rollDir * speed * Time.deltaTime);
        }
        else if (_attack != null && _attack.hitWindows != null)
        {
            // 클립 기준 진행도 (0~1)
            float clipT = _actionTime * _actionPlaySpeed / Mathf.Max(0.01f, _attack.clipLength);
            if (action == ActionState.LightAttack || action == ActionState.HeavyAttack || action == ActionState.LightCombo2) Lunge(clipT);
            for (int i = 0; i < _attack.hitWindows.Length; i++)
            {
                var w = _attack.hitWindows[i];
                if (clipT < w.x || clipT > w.y) continue;
                if (_hitWindowIndex != i)
                {
                    _hitWindowIndex = i;
                    if (!_attack.hitOncePerAction) _hitThisSwing.Clear();   // 구간마다 새로 맞음 (모션당 1회면 유지)
                }
                DoHit(action == ActionState.HeavyAttack);
            }
        }

        // 발도/납도: 정해진 지점에서 칼을 보이거나 숨김
        if ((action == ActionState.Draw || action == ActionState.Sheath) && !_visualSwitched && _attack != null)
        {
            float clipT = _actionTime * _actionPlaySpeed / Mathf.Max(0.01f, _attack.clipLength);
            float at = action == ActionState.Draw ? drawShowAt : sheathHideAt;
            if (clipT >= at || _actionTime >= _actionDuration)
            {
                _visualSwitched = true;
                SetDrawn(action == ActionState.Draw);
            }
        }

        if (_actionTime >= _actionDuration)
        {
            var next = _buffered;
            action = ActionState.None;
            _attack = null;
            if (animator) animator.speed = 1f;
            if (next != ActionState.None) StartAction(next);
            else if (animator) animator.CrossFadeInFixedTime(locomotionState, 0.2f);
            _locoState = locomotionState;
        }
    }

    // 휘두르는 순간(판정 구간 + 조금 앞)에만 앞으로 쭉 나감. 처음에 빠르고 끝에 감속
    void Lunge(float clipT)
    {
        float dist = _attack.lungeDistance;
        if (dist <= 0f) return;
        if (lockTarget && Flat(lockTarget.transform.position - transform.position).magnitude < lungeStopDistance) return;
        for (int i = 0; i < _attack.hitWindows.Length; i++)
        {
            float a = Mathf.Max(0f, _attack.hitWindows[i].x - lungeLead), b = _attack.hitWindows[i].y;
            if (clipT < a || clipT > b || b <= a) continue;
            float u = (clipT - a) / (b - a);                                  // 구간 안 진행도 0~1
            float seconds = (b - a) * _attack.clipLength / Mathf.Max(0.01f, _actionPlaySpeed);
            float speed = dist / Mathf.Max(0.01f, seconds) * 2f * (1f - u);  // 합계 = dist
            _cc.Move(transform.forward * speed * Time.deltaTime);
        }
    }

    void DoHit(bool heavy)
    {
        var w = weapons ? weapons.Current : null;
        float reach = w ? w.reach : 0.9f, radius = w ? w.hitRadius : 0.6f;
        float dmg = w ? (heavy ? w.heavyDamage : w.lightDamage) : (heavy ? 3f : 1f);
        Vector3 center = transform.position + Vector3.up * 0.5f + transform.forward * reach;
        foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
        {
            var d = col.GetComponentInParent<chg_Damageable>();
            if (!d || !d.IsAlive || _hitThisSwing.Contains(d)) continue;
            _hitThisSwing.Add(d);
            d.TakeDamage(dmg, transform.position);
            OnHitLanded?.Invoke(d);
            if (!_healedThisAction) { _healedThisAction = true; Heal(healOnHit); }   // 공격 1회당 한 번만 회복
        }
    }

    void FaceAttackDirection(Vector3 moveDir)
    {
        Vector3 dir = lockTarget ? Flat(lockTarget.transform.position - transform.position)
                    : (moveDir.sqrMagnitude > 0.01f ? moveDir : Vector3.zero);
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    void ApplyGravityOnly()
    {
        _vy = _cc.isGrounded ? -2f : _vy + gravity * Time.deltaTime;
        _cc.Move(Vector3.up * _vy * Time.deltaTime);
    }

    // ================================================================ 발도 / 락온

    void SetDrawn(bool value)
    {
        drawn = value;
        if (weapons) weapons.SetDrawn(drawn);
        if (!drawn) SetLockTarget(null);
        Debug.Log(drawn ? "[CHG] 발도 (전투 상태)" : "[CHG] 납도 (대기 상태)");
    }

    bool IsMeleeEquipped()
    {
        var w = weapons ? weapons.Current : null;
        return w == null || w.isMelee;
    }

    void ToggleLockOn()
    {
        if (lockTarget) { SetLockTarget(null); return; }
        if (!IsMeleeEquipped()) { Debug.Log("[CHG] 락온은 근접 무기만 가능"); return; }

        // 카메라 정면에 가깝고 가까운 대상 우선
        Vector3 camFwd = cameraTransform ? Flat(cameraTransform.forward).normalized : transform.forward;
        chg_Damageable best = null; float bestScore = float.MaxValue;
        foreach (var d in FindObjectsByType<chg_Damageable>())
        {
            if (!d.IsAlive) continue;
            Vector3 to = Flat(d.transform.position - transform.position);
            float dist = to.magnitude;
            if (dist > lockOnRange || dist < 0.01f) continue;
            float angle = Vector3.Angle(camFwd, to);
            if (angle > 80f) continue;
            float score = dist + angle * 0.05f;
            if (score < bestScore) { bestScore = score; best = d; }
        }
        if (!best) return;
        if (!drawn)
        {
            // 납도 상태: 발도 동작을 하면서 락온 (카메라는 바로 대상을 따라감)
            if (IsHitReacting || IsDead || IsStandingUp) return;
            if (action == ActionState.None) StartAction(ActionState.Draw);
            else if (action != ActionState.Draw && _buffered == ActionState.None) _buffered = ActionState.Draw;   // 지금 동작이 끝나면 발도
            if (!DrawPending()) return;   // 무기 교체 중 등으로 발도할 수 없으면 락온도 하지 않음
        }
        SetLockTarget(best);
    }

    // 발도가 진행 중이거나 예약돼 있는지 (납도 상태 공격 예약도 발도를 거침)
    bool DrawPending()
    {
        if (action == ActionState.Draw || _buffered == ActionState.Draw) return true;
        return _buffered == ActionState.LightAttack || _buffered == ActionState.HeavyAttack
            || _buffered == ActionState.LightCombo2 || _buffered == ActionState.Shot;
    }

    void ValidateLockOn()
    {
        if (!lockTarget) return;
        bool tooFar = Flat(lockTarget.transform.position - transform.position).magnitude > lockOnRange * 1.3f;
        // 발도가 피격 등으로 끊겨 납도 상태로 남으면 락온도 해제
        bool sheathedNoDraw = !drawn && !DrawPending();
        if (!lockTarget.isActiveAndEnabled || !lockTarget.IsAlive || tooFar || !IsMeleeEquipped() || sheathedNoDraw) SetLockTarget(null);
    }

    void SetLockTarget(chg_Damageable t)
    {
        if (lockTarget) lockTarget.SetLocked(false);
        lockTarget = t;
        if (lockTarget) lockTarget.SetLocked(true);
    }

    // ================================================================ 시간 역행 연동 (NGH_TimeRewind)
    // NGH(남귀훈) 추가: 시간 역행이 플레이어 내부 상태를 기록/복원할 수 있도록 하는 통로.
    // 위치·회전·애니메이터는 NGH_TimeRewind가 직접 기록/복원하고, 여기서는 이 스크립트 안의 상태만 다룬다.

    /// <summary>시간 역행용 플레이어 상태 (체력, 행동, 발도·납도, 무기 칸 등)</summary>
    [System.Serializable]
    public struct RewindState
    {
        public int hp;
        public bool drawn;
        public int weaponIndex;
        public ActionState action;
        public float actionTime, actionDuration, actionPlaySpeed;
        public ActionState buffered;
        public int hitWindowIndex;
        public bool visualSwitched, healedThisAction;
        public Vector3 rollDir;
        public float verticalVelocity;
        public string locoState;
        public float spawnInvincibleRemaining;
        public float deadTimeAgo;
        public chg_Damageable lockTarget;
    }

    /// <summary>외부 시스템이 무적을 켜고 끔. 켠 쪽(source)별로 관리하며, 모두 끄면 해제된다.</summary>
    public void SetExternalInvincible(object source, bool on)
    {
        if (source == null) return;
        if (on) _externalInvincible.Add(source);
        else _externalInvincible.Remove(source);
    }

    /// <summary>현재 상태를 기록용으로 복사</summary>
    public RewindState CaptureRewindState()
    {
        return new RewindState
        {
            hp = hp,
            drawn = drawn,
            weaponIndex = weapons ? weapons.currentIndex : 0,
            action = action,
            actionTime = _actionTime,
            actionDuration = _actionDuration,
            actionPlaySpeed = _actionPlaySpeed,
            buffered = _buffered,
            hitWindowIndex = _hitWindowIndex,
            visualSwitched = _visualSwitched,
            healedThisAction = _healedThisAction,
            rollDir = _rollDir,
            verticalVelocity = _vy,
            locoState = _locoState,
            spawnInvincibleRemaining = Mathf.Max(0f, _spawnInvincibleUntil - Time.time),
            deadTimeAgo = Time.time - DeadTime,
            lockTarget = lockTarget,
        };
    }

    /// <summary>기록해 둔 상태로 되돌림 (체력, 행동, 발도·납도, 무기 칸, 락온)</summary>
    public void RestoreRewindState(RewindState s)
    {
        SetLockTarget(null);
        _hitThisSwing.Clear();
        SetHp(s.hp);

        drawn = s.drawn;
        if (weapons) weapons.RestoreState(s.weaponIndex, drawn);

        action = s.action;
        _attack = AttackDataFor(s.action);
        _actionTime = s.actionTime;
        _actionDuration = s.actionDuration;
        _actionPlaySpeed = s.actionPlaySpeed;
        _buffered = s.buffered;
        _hitWindowIndex = s.hitWindowIndex;
        _visualSwitched = s.visualSwitched;
        _healedThisAction = s.healedThisAction;
        _rollDir = s.rollDir;
        _vy = s.verticalVelocity;
        _locoState = string.IsNullOrEmpty(s.locoState) ? locomotionState : s.locoState;
        _spawnInvincibleUntil = Time.time + s.spawnInvincibleRemaining;
        DeadTime = Time.time - s.deadTimeAgo;

        // 락온: 대상이 아직 살아 있고, 발도 상태에서 근접 무기를 들고 있을 때만 되살림
        chg_Damageable lt = s.lockTarget;
        if (lt && lt.isActiveAndEnabled && lt.IsAlive && drawn && IsMeleeEquipped()) SetLockTarget(lt);
    }

    AttackData AttackDataFor(ActionState a)
    {
        switch (a)
        {
            case ActionState.LightAttack: return lightAttack;
            case ActionState.HeavyAttack: return heavyAttack;
            case ActionState.LightCombo2: return lightCombo2;
            case ActionState.Shot: return shotAttack;
            case ActionState.Draw: return drawAction;
            case ActionState.Sheath: return sheathAction;
            case ActionState.HitSmall: return hitSmallAction;
            case ActionState.HitLarge: return hitLargeAction;
            default: return null;
        }
    }

    // ================================================================ 유틸

    static Vector2 ReadMoveInput()
    {
        var kb = Keyboard.current;
        Vector2 v = Vector2.zero;
        if (kb == null) return v;
        if (kb.wKey.isPressed) v.y += 1;
        if (kb.sKey.isPressed) v.y -= 1;
        if (kb.dKey.isPressed) v.x += 1;
        if (kb.aKey.isPressed) v.x -= 1;
        return v.sqrMagnitude > 1f ? v.normalized : v;   // 대각선도 같은 속도
    }

    Vector3 CameraRelative(Vector2 input)
    {
        Vector3 fwd = cameraTransform ? Flat(cameraTransform.forward) : Vector3.forward;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return fwd * input.y + right * input.x;
    }

    void TurnTowards(Vector3 dir)
    {
        var target = Quaternion.LookRotation(Flat(dir));
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    void OnDrawGizmosSelected()
    {
        var w = weapons ? weapons.Current : null;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f + transform.forward * (w ? w.reach : 0.9f),
                              w ? w.hitRadius : 0.6f);
    }
}
