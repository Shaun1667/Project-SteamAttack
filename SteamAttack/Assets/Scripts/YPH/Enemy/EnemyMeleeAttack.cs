using System.Collections;
using UnityEngine;
using NGH;
using Random = UnityEngine.Random;

namespace YPH
{
    /// <summary>일반·강공격·킥의 순서와 판정 상자를 관리합니다. 킥은 원거리 적의 근접 대응으로 남깁니다.</summary>
    [DisallowMultipleComponent]
    public class EnemyMeleeAttack : EnemyAttack
    {
        [Header("일반 공격")]
        [SerializeField, Tooltip("이 거리(m) 안에서 일반 공격을 시작합니다. 높이면 멀리서 공격합니다.")] private float _attackRange = 1.8f;
        [SerializeField, Tooltip("판정 전 대기 시간(초)입니다. 클립 이벤트가 있으면 그 시각이 우선합니다.")] private float _attackWindup = 0.4f;
        [SerializeField, Tooltip("판정 상자가 유지되는 시간(초)입니다. 높이면 맞출 시간이 길어집니다.")] private float _attackActiveTime = 0.15f;
        [SerializeField, Tooltip("일반 공격 후 대기 시간(초)입니다. 높이면 다음 공격이 늦어집니다.")] private float _attackCooldown = 1.2f;
        [SerializeField, Tooltip("일반 공격 피해입니다. 높이면 플레이어 HP가 더 많이 줄어듭니다.")] private int _attackDamage = 22;
        [SerializeField, Tooltip("일반 공격에 맞은 플레이어의 경직 단계입니다.")] private HitStagger _attackStagger = HitStagger.Light;
        [SerializeField, Tooltip("풀에서 꺼낼 판정 상자입니다. 비우면 공격 판정이 생기지 않습니다.")] private EnemyAttackHitbox _attackHitboxPrefab;
        [SerializeField, Tooltip("적 로컬 좌표에서 판정 중심 위치(m)입니다. Z를 높이면 앞에서 맞춥니다.")] private Vector3 _attackHitboxOffset = new Vector3(0f, 0f, 1.1f);
        [SerializeField, Tooltip("판정 상자의 가로·높이·깊이(m)입니다. 높이면 맞는 범위가 넓어집니다.")] private Vector3 _attackHitboxSize = new Vector3(1.2f, 1.5f, 1.2f);
        [SerializeField, Tooltip("시작할 때 미리 만들어 둘 판정 상자 수입니다. 높이면 첫 생성 부담을 미리 치릅니다.")] private int _attackHitboxPrewarm = 2;
        [SerializeField, Min(0f), Tooltip("일반 공격 순간 앞으로 나갈 거리(m)입니다. 0이면 전진하지 않습니다.")] private float _attackLungeDistance = 1f;
        [SerializeField, Min(0.01f), Tooltip("일반 공격 전진 시간(초)입니다. 낮추면 같은 거리를 더 빠르게 갑니다.")] private float _attackLungeDuration = 0.12f;
        [Header("강공격")]
        [SerializeField, Tooltip("켜면 일반 공격 대신 확률로 강공격을 고릅니다.")] private bool _heavyEnabled;
        [SerializeField, Range(0f, 1f), Tooltip("공격마다 강공격을 고를 확률입니다. 1이면 항상 강공격입니다.")] private float _heavyChance = 0.3f;
        [SerializeField, Tooltip("강공격 피해입니다. 높이면 플레이어 HP가 더 많이 줄어듭니다.")] private int _heavyDamage = 44;
        [SerializeField, Tooltip("강공격에 맞은 플레이어의 경직 단계입니다.")] private HitStagger _heavyStagger = HitStagger.Heavy;
        [SerializeField, Tooltip("강공격을 시작할 거리(m)입니다. 높이면 더 멀리서 달려듭니다.")] private float _heavyStartRange = 4.5f;
        [SerializeField, Tooltip("강공격 판정까지 시간(초)입니다. 클립 이벤트가 있으면 그 시각을 씁니다.")] private float _heavyWindup = 1.62f;
        [SerializeField, Tooltip("강공격 판정 유지 시간(초)입니다.")] private float _heavyActiveTime = 0.15f;
        [SerializeField, Tooltip("강공격 후 대기 시간(초)입니다. 클립 이벤트가 있으면 클립의 남은 길이를 씁니다.")] private float _heavyRecovery = 1.9f;
        [SerializeField, Min(0f), Tooltip("강공격 최대 전진 거리(m)입니다. 플레이어 표면 앞에서 멈춥니다.")] private float _heavyDashDistance = 4.3f;
        [SerializeField, Min(0.01f), Tooltip("강공격 전진 시간(초)입니다. 클립 이벤트가 있으면 판정 시각까지 전진합니다.")] private float _heavyDashDuration = 1.6f;
        [SerializeField, Tooltip("강공격 판정 중심의 로컬 위치(m)입니다.")] private Vector3 _heavyHitboxOffset = new Vector3(0f, 0.9f, 1.1f);
        [SerializeField, Tooltip("강공격 판정 상자의 크기(m)입니다. 높이면 범위가 넓어집니다.")] private Vector3 _heavyHitboxSize = new Vector3(1.4f, 1.8f, 1.6f);
        [SerializeField, Tooltip("켜면 강공격 예고 때 동심원이 나타나며 받아치기를 허용합니다.")] private bool _heavyParryable = true;
        [Header("원거리 적의 근접 킥")]
        [SerializeField, Tooltip("켜면 원거리 모드에서 가까운 플레이어에게 사격보다 킥을 먼저 사용합니다.")] private bool _kickEnabled;
        [SerializeField, Tooltip("킥 시작 거리(m)입니다. 높이면 더 멀리서 킥을 고릅니다.")] private float _kickRange = 2.5f;
        [SerializeField, Tooltip("킥 피해입니다. 높이면 플레이어 HP가 더 많이 줄어듭니다.")] private int _kickDamage = 25;
        [SerializeField, Tooltip("킥에 맞은 플레이어의 경직 단계입니다.")] private HitStagger _kickStagger = HitStagger.Heavy;
        [SerializeField, Tooltip("킥 시작부터 다음 킥까지 최소 간격(초)입니다.")] private float _kickCooldown = 4f;
        [SerializeField, Tooltip("킥 준비 자세 시간(초)입니다. 높이면 받아치기 예고가 길어집니다.")] private float _kickPrepTime = 0.7f;
        [SerializeField, Range(0f, 1f), Tooltip("준비 자세의 클립 속도입니다. 낮추면 멈춘 듯 느리게 움직입니다.")] private float _kickPrepAnimSpeed = 0.15f;
        [SerializeField, Tooltip("킥 클립에서 발이 닿는 시각(초)입니다. 이벤트가 있으면 이벤트가 우선합니다.")] private float _kickHitClipTime = 0.3f;
        [SerializeField, Tooltip("킥 판정 유지 시간(초)입니다.")] private float _kickActiveTime = 0.13f;
        [SerializeField, Tooltip("킥 클립 길이(초)입니다. 높이면 판정 후 복귀가 늦어지며 이벤트가 있으면 실제 클립 길이를 씁니다.")] private float _kickClipLength = 0.8f;
        [SerializeField, Tooltip("킥 판정 순간 전진 거리(m)입니다. 0이면 제자리에서 찹니다.")] private float _kickLungeDistance = 0.3f;
        [SerializeField, Tooltip("킥 판정 중심의 로컬 위치(m)입니다.")] private Vector3 _kickHitboxOffset = new Vector3(0f, 1f, 0.9f);
        [SerializeField, Tooltip("킥 판정 상자의 크기(m)입니다. 높이면 범위가 넓어집니다.")] private Vector3 _kickHitboxSize = new Vector3(1f, 1f, 1.2f);
        [SerializeField, Tooltip("켜면 킥 준비 때 동심원을 띄우고 받아치기를 허용합니다.")] private bool _kickParryable = true;
        [SerializeField, Range(-45f, 45f), Tooltip("발이 향하는 방향 보정(도)입니다. 양수이면 몸을 오른쪽으로 더 돌립니다.")] private float _kickFacingOffset = 10f;
        private bool _attackChosen, _nextIsHeavy;
        private float _nextKickTime;
        private float AttackWindup => Animation != null && Animation.AttackHitTime >= 0f ? Animation.AttackHitTime : _attackWindup;
        private float HeavyWindup => Animation != null && Animation.HeavyHitTime >= 0f ? Animation.HeavyHitTime : _heavyWindup;
        private float HeavyDashTime => Animation != null && Animation.HeavyHitTime >= 0f ? Animation.HeavyHitTime : _heavyDashDuration;
        private float HeavyRecovery => Animation != null && Animation.HeavyLength >= 0f
            ? Mathf.Max(0f, Animation.HeavyLength - Animation.HeavyHitTime - _heavyActiveTime) : _heavyRecovery;
        private float KickHitTime => Animation != null && Animation.KickHitTime >= 0f ? Animation.KickHitTime : _kickHitClipTime;
        private float KickLength => Animation != null && Animation.KickLength >= 0f ? Animation.KickLength : _kickClipLength;

        private void Start()
        {
            if (_attackHitboxPrefab != null)
            {
                ObjectPool.Prewarm(_attackHitboxPrefab.gameObject, _attackHitboxPrewarm);
            }
        }
        private void OnValidate()
        {
            ClampRange(GetComponent<EnemyAI>().DetectRange);
            _attackHitboxPrewarm = Mathf.Max(0, _attackHitboxPrewarm);
            _heavyStartRange = Mathf.Max(0.1f, _heavyStartRange);
            _heavyWindup = Mathf.Max(0f, _heavyWindup);
            _heavyActiveTime = Mathf.Max(0.01f, _heavyActiveTime);
            _heavyRecovery = Mathf.Max(0f, _heavyRecovery);
        }
        public override bool CanBegin(float distanceToPlayer)
        {
            if (Brain.UsesRanged)
            {
                return _kickEnabled && distanceToPlayer <= _kickRange && Time.time >= _nextKickTime;
            }
            // 접근 중 매 프레임 다시 뽑으면 강공격 확률이 달라지므로 한 공격 주기 동안 선택을 보존합니다.
            if (!_attackChosen)
            {
                _nextIsHeavy = _heavyEnabled && Random.value < _heavyChance;
                _attackChosen = true;
            }
            return distanceToPlayer <= (_nextIsHeavy ? _heavyStartRange : _attackRange);
        }
        public override void Begin()
        {
            Run(Brain.UsesRanged ? KickRoutine() : _nextIsHeavy ? HeavyRoutine() : NormalRoutine());
        }
        public override void Cancel()
        {
            StopSequence();
        }
        internal void ResetChoice() => _attackChosen = false;
        // 인지 거리와 공격 거리를 서로 다른 컴포넌트에서 편집해도 기존 보정 규칙을 유지합니다.
        internal void ClampRange(float detectRange) => _attackRange = Mathf.Clamp(_attackRange, 0.1f, Mathf.Max(0.1f, detectRange));
        internal void ResetCooldown()
        {
            ResetChoice();
            _nextKickTime = 0f;
        }

        private IEnumerator NormalRoutine()
        {
            Animation?.Play(EnemyAnimatorLink.AttackHash);
            yield return WaitActive(AttackWindup, true);
            SpawnHitbox(_attackDamage, _attackStagger, _attackHitboxOffset, _attackHitboxSize, _attackActiveTime, "근접 공격");
            Motor.StartLunge(_attackLungeDistance, _attackLungeDuration, false);
            yield return WaitActive(_attackActiveTime);
            yield return WaitActive(_attackCooldown, true);
            Finish();
        }
        private IEnumerator HeavyRoutine()
        {
            Animation?.Play(EnemyAnimatorLink.HeavyAttackHash);
            Brain.FacePlayer();
            Motor.StartLunge(_heavyDashDistance, HeavyDashTime, true);
            Brain.SetSuperArmor(true);
            if (_heavyParryable)
            {
                Brain.OpenParryWindow(Time.time + HeavyWindup);
            }
            yield return WaitActive(HeavyWindup);
            SpawnHitbox(_heavyDamage, _heavyStagger, _heavyHitboxOffset, _heavyHitboxSize, _heavyActiveTime, "강공격");
            yield return WaitActive(_heavyActiveTime);
            Brain.CloseParryWindow();
            yield return WaitActive(HeavyRecovery);
            Finish();
        }
        private IEnumerator KickRoutine()
        {
            Brain.SetSuperArmor(true);
            _nextKickTime = Time.time + _kickCooldown;
            Animation?.SetKickSpeed(_kickPrepAnimSpeed);
            Animation?.Play(EnemyAnimatorLink.KickHash);
            if (_kickParryable)
            {
                float untilHit = _kickPrepTime + Mathf.Max(0f, KickHitTime - _kickPrepTime * _kickPrepAnimSpeed);
                Brain.OpenParryWindow(Time.time + untilHit);
            }
            Brain.Log("킥 준비");
            float timer = 0f;
            while (timer < _kickPrepTime)
            {
                Brain.FacePlayer();
                Motor.Face(Quaternion.Euler(0f, _kickFacingOffset, 0f) * Motor.Facing);
                timer += Brain.ActiveDeltaTime;
                yield return null;
            }
            Animation?.SetKickSpeed(1f);
            yield return WaitActive(Mathf.Max(0f, KickHitTime - _kickPrepTime * _kickPrepAnimSpeed));
            SpawnHitbox(_kickDamage, _kickStagger, _kickHitboxOffset, _kickHitboxSize, _kickActiveTime, "킥");
            Motor.StartLunge(_kickLungeDistance, 0.12f, false);
            yield return WaitActive(_kickActiveTime);
            Brain.CloseParryWindow();
            yield return WaitActive(Mathf.Max(0f, KickLength - KickHitTime - _kickActiveTime));
            Finish();
        }
        private void SpawnHitbox(int damage, HitStagger stagger, Vector3 offset, Vector3 size, float activeTime, string label)
        {
            if (_attackHitboxPrefab == null)
            {
                Debug.LogWarning("[CHG_EnemyAI:" + name + "] Attack Hitbox Prefab이 비어 있어 공격 판정을 만들 수 없습니다.", this);
                return;
            }
            EnemyAttackHitbox hitbox = ObjectPool.Spawn(_attackHitboxPrefab, transform.TransformPoint(offset), transform.rotation);
            if (hitbox != null)
            {
                // 전진 중에도 몸을 따라가도록 판정 상자에 기준 Transform을 전달합니다.
                hitbox.Init(gameObject, damage, size, activeTime, transform, offset, stagger);
                Brain.Log(label + " (데미지 " + damage + ", 경직 " + stagger + ")");
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal void DrawDebugLines()
        {
            Vector3 center = transform.position + Vector3.up * 0.05f;
            if (Brain.UsesRanged)
            {
                if (_kickEnabled)
                {
                    DebugView.Circle(center, _kickRange, Color.magenta);
                }
            }
            else
            {
                DebugView.Circle(center, _attackRange, Color.red);
                if (_heavyEnabled)
                {
                    DebugView.Circle(center, _heavyStartRange, Color.magenta);
                }
            }
        }
#endif
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying)
            {
                return;
            }
            EnemyDebugTools.DrawCircle(transform.position, _attackRange, Color.red);
            EnemyDebugTools.DrawHitbox(transform, _attackHitboxOffset, _attackHitboxSize, Color.red);
            Gizmos.color = new Color(0.8f, 0.3f, 1f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * _attackLungeDistance);
            if (_heavyEnabled)
            {
                EnemyDebugTools.DrawCircle(transform.position, _heavyStartRange, Color.magenta);
                EnemyDebugTools.DrawHitbox(transform, _heavyHitboxOffset, _heavyHitboxSize, Color.magenta);
            }
            if (_kickEnabled)
            {
                EnemyDebugTools.DrawCircle(transform.position, _kickRange, Color.magenta);
                EnemyDebugTools.DrawHitbox(transform, _kickHitboxOffset, _kickHitboxSize, Color.magenta);
            }
        }
#endif
    }
}
