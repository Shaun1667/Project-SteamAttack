using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 근접 공격 판정용 히트박스 (NGH)
/// - CHG_ObjectPool로 꺼내서 쓰고, 지정된 시간이 지나면 풀에 반납된다.
/// - 태그는 EnemyAttack. 플레이어 쪽에서는 이 태그로 피격을 판단한다.
/// - Kinematic Rigidbody가 붙어 있어 상대에게 Rigidbody가 없어도 트리거 이벤트가 발생한다.
/// - followTarget을 지정하면 공격 중 적이 전진해도 히트박스가 따라간다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class CHG_EnemyAttackHitbox : MonoBehaviour, CHG_IPoolable
{
    public const string AttackTag = "EnemyAttack";

    /// <summary>대상에게 명중했을 때 (히트박스, 맞은 대상)</summary>
    public static event Action<CHG_EnemyAttackHitbox, GameObject> OnHit;

    [Tooltip("명중 대상 태그")]
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool logHits = true;
    [Tooltip("명중한 대상이 CHG 플레이어면 피격 함수(TakeHit)를 호출해 실제로 데미지를 준다")]
    [SerializeField] private bool applyDamageToPlayer = true;

    public int Damage { get; private set; } = 1;
    public GameObject Owner { get; private set; }

    private readonly HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private BoxCollider box;
    private Rigidbody body;

    private Transform followTarget;
    private Rigidbody followBody;
    private Vector3 followLocalOffset;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;

        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        if (!CompareTag(AttackTag))
        {
            gameObject.tag = AttackTag;
        }
    }

    /// <summary>
    /// Spawn 직후 호출. 크기/데미지를 설정하고 lifetime 뒤에 풀로 반납된다.
    /// follow를 넘기면 그 대상 기준 localOffset 위치를 계속 따라간다.
    /// </summary>
    public void Init(GameObject owner, int damage, Vector3 size, float lifetime, Transform follow = null, Vector3 localOffset = default)
    {
        Owner = owner;
        Damage = damage;
        box.center = Vector3.zero;
        box.size = size;

        followTarget = follow;
        followBody = follow != null ? follow.GetComponent<Rigidbody>() : null;
        followLocalOffset = localOffset;

        hitTargets.Clear();
        CHG_ObjectPool.Despawn(gameObject, Mathf.Max(0.01f, lifetime));
        CheckOverlapNow();
    }

    public void OnSpawned()
    {
        hitTargets.Clear();
    }

    public void OnDespawned()
    {
        Owner = null;
        followTarget = null;
        followBody = null;
        hitTargets.Clear();
    }

    private void FixedUpdate()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 basePosition = followBody != null ? followBody.position : followTarget.position;
        Quaternion baseRotation = followBody != null ? followBody.rotation : followTarget.rotation;
        Vector3 scaledOffset = Vector3.Scale(followLocalOffset, followTarget.lossyScale);

        body.MovePosition(basePosition + baseRotation * scaledOffset);
        body.MoveRotation(baseRotation);
    }

    // 생성 순간 이미 겹쳐 있는 대상도 놓치지 않도록 즉시 한 번 검사
    private void CheckOverlapNow()
    {
        Vector3 center = transform.TransformPoint(box.center);
        Vector3 halfExtents = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
        Collider[] hits = Physics.OverlapBox(center, halfExtents, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            TryHit(hit);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    private void TryHit(Collider other)
    {
        if (other == null)
        {
            return;
        }

        // 자기 자신(공격한 적)은 무시
        if (Owner != null && other.transform.IsChildOf(Owner.transform))
        {
            return;
        }

        GameObject target = null;
        if (other.CompareTag(targetTag))
        {
            target = other.gameObject;
        }
        else if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(targetTag))
        {
            target = other.attachedRigidbody.gameObject;
        }

        // 한 번의 공격에 같은 대상은 한 번만
        if (target == null || !hitTargets.Add(target))
        {
            return;
        }

        bool damaged = applyDamageToPlayer && ApplyDamage(target);

        if (logHits)
        {
            string ownerName = Owner != null ? Owner.name : "Unknown";
            string resultText = applyDamageToPlayer && !damaged ? " - 피해 없음 (무적 상태)" : "";
            Debug.Log("[CHG_EnemyAttackHitbox] " + ownerName + " → " + target.name + " 명중 (데미지 " + Damage + ")" + resultText, target);
        }

        OnHit?.Invoke(this, target);
    }

    // 맞은 대상의 플레이어 피격 함수를 호출한다. 실제로 피해가 들어갔으면 true (구르기·피격 중 무적이면 false)
    private bool ApplyDamage(GameObject target)
    {
        NGH_PlayerController playerController = target.GetComponentInParent<NGH_PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        Vector3 from = Owner != null ? Owner.transform.position : transform.position;
        return playerController.TakeHit(Damage, from);
    }

    private void OnDrawGizmos()
    {
        BoxCollider gizmoBox = box != null ? box : GetComponent<BoxCollider>();
        if (gizmoBox == null || !gizmoBox.enabled || !gameObject.activeInHierarchy)
        {
            return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawCube(gizmoBox.center, gizmoBox.size);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(gizmoBox.center, gizmoBox.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
