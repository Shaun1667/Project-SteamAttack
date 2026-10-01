using System.Collections.Generic;
using UnityEngine;

/// <summary>던져진 수류탄의 기폭 시점과 범위 피해를 처리합니다. 소지 수나 제작 비용은 모릅니다.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class YPH_Grenade : MonoBehaviour
{
    /// <summary>던진 뒤 시간으로 터뜨릴지, 첫 물리 충돌에서 터뜨릴지 선택합니다.</summary>
    public enum DetonationMode { AfterThrow, OnImpact }
    private const int HitBufferSize = 32;

    [SerializeField, Tooltip("AfterThrow는 던진 뒤 지연 폭발, OnImpact는 소유자 외 물체에 처음 닿을 때 폭발합니다.")]
    private DetonationMode _detonationMode = DetonationMode.AfterThrow;
    [SerializeField, Min(0f), Tooltip("AfterThrow에서 던진 뒤 폭발까지의 초입니다. 0이면 던지는 즉시 폭발하며 비행 중 변경도 반영합니다.")]
    private float _fuseTime = 2f;
    [SerializeField, Min(0.1f), Tooltip("충돌하지 않아도 폭발시키는 최대 생존 초입니다. 도화선 시간보다 짧으면 도화선 시간을 사용합니다.")]
    private float _maxLifetime = 10f;
    [SerializeField, Min(0.01f), Tooltip("폭발 위치에서 피해를 검사하는 반경(m)입니다. 표시 구의 지름은 이 값의 두 배입니다.")]
    private float _explosionRadius = 4f;
    [SerializeField, Min(0f), Tooltip("범위 안의 살아 있는 적·보스 한 명당 피해입니다. 콜라이더 수와 무관하게 한 번 주며 0이면 피해·회복이 없습니다.")]
    private float _enemyDamage = 3f;
    [SerializeField, Min(0f), Tooltip("범위 안 플레이어에게 주는 피해입니다. 0이면 플레이어 피해를 끄고, 어떤 값이든 플레이어 피격은 증기를 회복하지 않습니다.")]
    private float _playerDamage = 1f;
    [SerializeField, Tooltip("폭발 범위 검사를 할 레이어입니다. 제외한 레이어의 대상은 범위 안에 있어도 피해를 받지 않습니다.")]
    private LayerMask _hitMask = ~0;
    [SerializeField, Tooltip("폭발 반경을 보여 줄 선택 프리팹입니다. 비워 두어도 피해는 정상 처리합니다.")]
    private YPH_ExplosionFlash _explosionFlashPrefab;

    private Rigidbody _rigidbody;
    private SphereCollider _collider;
    private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
    // 폭발 중 다른 폭발이 발생해도 서로의 중복 목록을 지우지 않도록 수류탄마다 보관합니다.
    private readonly List<YPH_IDamageable> _alreadyHit = new List<YPH_IDamageable>(HitBufferSize);
    private YPH_SteamHitRefill _hitRefill;
    private float _lifeTimer;
    private bool _launched;
    private bool _exploded;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<SphereCollider>();
    }

    private void Update()
    {
        if (!_launched || _exploded) return;
        _lifeTimer += Time.deltaTime;
        if ((_detonationMode == DetonationMode.AfterThrow && _lifeTimer >= _fuseTime)
            || _lifeTimer >= Mathf.Max(_maxLifetime, _fuseTime)) Explode(transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_launched || _exploded || _detonationMode != DetonationMode.OnImpact) return;
        Explode(collision.GetContact(0).point); // 중심 대신 닿은 자리에서 반경을 계산합니다.
    }

    /// <summary>초기 속도와 회복 입구를 받고 도화선을 시작합니다. 소유자 충돌만 무시하며 폭발 피해에서는 제외하지 않습니다.</summary>
    public void Launch(Vector3 velocity, YPH_SteamHitRefill hitRefill, Transform ignoreRoot)
    {
        if (!isActiveAndEnabled || _launched) return;
        _hitRefill = hitRefill;
        if (ignoreRoot != null)
        {
            // 한 번만 찾습니다. 비활성 몸체도 포함해, 비행 도중 플레이어가 부활해도 자기 몸에 걸리지 않게 합니다.
            foreach (Collider owner in ignoreRoot.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(_collider, owner);
        }
        _rigidbody.linearVelocity = velocity;
        _lifeTimer = 0f;
        _launched = true;
        if (_detonationMode == DetonationMode.AfterThrow && _fuseTime <= 0f) Explode(transform.position);
    }

    private void Explode(Vector3 position)
    {
        if (_exploded) return;
        _exploded = true; // 피해 콜백이나 같은 프레임의 충돌이 다시 폭발을 실행하지 못하게 먼저 잠급니다.
        int count = Physics.OverlapSphereNonAlloc(position, _explosionRadius, _hitBuffer, _hitMask, QueryTriggerInteraction.Ignore);
        _alreadyHit.Clear();
        // ponytail: 한 폭발에 콜라이더 32개까지 검사합니다. 밀집 전투로 이 한도를 넘으면 버퍼 정책을 재검토합니다.
        for (int i = 0; i < count; i++)
        {
            YPH_IDamageable target = _hitBuffer[i].GetComponentInParent<YPH_IDamageable>();
            if (target == null || _alreadyHit.Contains(target)) continue;
            _alreadyHit.Add(target);
            if (!target.IsAlive) continue;
            float damage = YPH_CombatTags.IsEnemy(target) ? _enemyDamage : YPH_CombatTags.IsPlayer(target) ? _playerDamage : 0f;
            if (damage <= 0f) continue;
            target.TakeDamage(damage, position);
            if (_hitRefill != null) _hitRefill.NotifyHit(target); // 적만 회복하는 규칙은 공통 입구에 있습니다.
        }
        if (_explosionFlashPrefab != null)
            Instantiate(_explosionFlashPrefab, position, Quaternion.identity).Show(_explosionRadius);
        Destroy(gameObject);
    }
}
