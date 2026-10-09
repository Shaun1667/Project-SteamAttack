using UnityEngine;

/// <summary>
/// 적 총알 (원거리 병사용). CHG_ObjectPool로 꺼내 Fire()로 발사하고, 맞거나 사거리를 넘으면 풀로 반납된다.
/// - 매 프레임 이동 거리만큼 SphereCast로 검사하므로 빨라도 벽·플레이어를 뚫지 않는다.
/// - 플레이어(NGH_PlayerController)에 맞으면 TakeHit(데미지, 쏜 위치)를 호출한다.
///   구르기 등으로 무적이면 피해 없이 통과한다.
/// - 쏜 적 자신과 다른 적, 트리거 콜라이더는 무시하고, 그 밖의 콜라이더(벽·바닥)에 닿으면 사라진다.
/// - TrailRenderer가 있으면 궤적을 남기고, 멈춘 뒤 궤적이 다 사라지면 반납된다.
/// </summary>
[DisallowMultipleComponent]
public class chg_EnemyBullet : MonoBehaviour, CHG_IPoolable
{
    [Tooltip("총알이 부딪히는 레이어")]
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("총알 머리(보이는 구) — 멈추면 바로 숨김")]
    [SerializeField] private Renderer headRenderer;
    [SerializeField] private bool logHits = true;

    private static readonly RaycastHit[] hits = new RaycastHit[16];

    private TrailRenderer trail;
    private GameObject owner;
    private Vector3 origin;
    private Vector3 direction;
    private int damage;
    private float speed;
    private float range;
    private float radius;
    private float traveled;
    private bool flying;
    private Transform ignoredPlayer;   // 무적이라 통과한 플레이어는 다시 맞지 않음

    private void Awake()
    {
        trail = GetComponentInChildren<TrailRenderer>(true);
        if (headRenderer == null)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is TrailRenderer))
                {
                    headRenderer = r;
                    break;
                }
            }
        }
    }

    /// <summary>Spawn 직후 호출. dir 방향으로 speed(m/s)로 날아가며 range를 넘으면 사라진다.</summary>
    public void Fire(GameObject shooter, int bulletDamage, Vector3 dir, float bulletSpeed, float maxRange, float hitRadius)
    {
        owner = shooter;
        damage = bulletDamage;
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        speed = Mathf.Max(0.1f, bulletSpeed);
        range = Mathf.Max(0.1f, maxRange);
        radius = Mathf.Max(0.01f, hitRadius);
        origin = transform.position;
        traveled = 0f;
        ignoredPlayer = null;
        flying = true;

        transform.rotation = Quaternion.LookRotation(direction);
        if (headRenderer != null)
        {
            headRenderer.enabled = true;
        }
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    public void OnSpawned()
    {
        flying = false;
        if (trail != null)
        {
            trail.Clear();
        }
    }

    public void OnDespawned()
    {
        flying = false;
        owner = null;
        ignoredPlayer = null;
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }
    }

    private void Update()
    {
        if (!flying)
        {
            return;
        }

        float step = Mathf.Min(speed * Time.deltaTime, range - traveled);
        Vector3 position = transform.position;

        if (step > 0f && CheckHit(position, step, out RaycastHit hit))
        {
            transform.position = position + direction * hit.distance;
            OnHitCollider(hit.collider);
            if (!flying)
            {
                return;
            }
        }

        transform.position = position + direction * step;
        traveled += step;
        if (traveled >= range - 0.0001f)
        {
            Stop();
        }
    }

    // 이동 경로에서 가장 가까운 유효한 충돌 찾기
    private bool CheckHit(Vector3 from, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, hitMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || IsIgnored(col))
            {
                continue;
            }
            // 시작 지점에서 이미 겹친 경우 distance가 0으로 나온다
            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                best = hits[i];
                found = true;
            }
        }
        return found;
    }

    private bool IsIgnored(Collider col)
    {
        if (owner != null && col.transform.IsChildOf(owner.transform))
        {
            return true;
        }
        if (ignoredPlayer != null && col.transform.IsChildOf(ignoredPlayer))
        {
            return true;
        }
        // 다른 적끼리는 맞지 않음
        return col.GetComponentInParent<CHG_EnemyHealth>() != null;
    }

    private void OnHitCollider(Collider col)
    {
        NGH_PlayerController player = col.GetComponentInParent<NGH_PlayerController>();
        if (player != null)
        {
            Vector3 from = owner != null ? owner.transform.position : origin;
            bool damaged = player.TakeHit(damage, from);
            if (logHits)
            {
                string ownerName = owner != null ? owner.name : "Unknown";
                Debug.Log("[chg_EnemyBullet] " + ownerName + " → " + player.name + (damaged ? " 명중 (데미지 " + damage + ")" : " 통과 (무적 상태)"), player);
            }
            if (!damaged)
            {
                // 구르기 무적이면 그대로 통과
                ignoredPlayer = player.transform;
                return;
            }
        }
        Stop();
    }

    private void Stop()
    {
        flying = false;
        if (headRenderer != null)
        {
            headRenderer.enabled = false;
        }

        float wait = 0f;
        if (trail != null)
        {
            trail.emitting = false;
            wait = trail.time;
        }
        CHG_ObjectPool.Despawn(gameObject, wait);
    }
}
