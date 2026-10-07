using UnityEngine;

/// <summary>
/// 병합용 연결 (NGH) — 적 HP(CHG_EnemyHealth)를 다른 파트의 공격과 잇는다.
/// - YPH 총·수류탄: YPH_IDamageable로 피해를 받아 CHG_EnemyHealth.TakeDamage로 넘긴다.
/// - CHG 근접: CHG 근접 판정은 NGH_Damageable만 찾으므로, 같은 오브젝트의 NGH_Damageable을 "피격 수신용"으로 쓴다.
///   깎인 만큼을 CHG_EnemyHealth로 옮기고 수신용 HP는 다시 채운다. (락온 표시도 NGH_Damageable이 담당)
/// - 적이 쓰러지면 수신용 NGH_Damageable을 꺼서 락온과 근접 판정에서 빠지게 하고, 부활하면 다시 켠다.
/// CHG 근접이 공통 피해 인터페이스를 쓰게 되면 근접 연결 부분은 제거한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CHG_EnemyHealth))]
public class CHG_EnemyDamageRelay : MonoBehaviour, YPH_IDamageable
{
    [Tooltip("CHG 근접 피격을 받을 NGH_Damageable. 비우면 같은 오브젝트에서 찾고, 없으면 근접 연결은 하지 않음")]
    [SerializeField] private NGH_Damageable meleeReceiver;
    [Tooltip("수신용 NGH_Damageable의 HP. 한 번에 다 깎이지 않도록 크게 둔다")]
    [SerializeField, Min(10f)] private float receiverHp = 1000f;

    private CHG_EnemyHealth health;
    private Transform player;

    public bool IsAlive => health != null && health.IsAlive;

    public void TakeDamage(float amount, Vector3 hitFrom)
    {
        if (health != null)
        {
            health.TakeDamage(amount, hitFrom);
        }
    }

    private void Awake()
    {
        health = GetComponent<CHG_EnemyHealth>();
        if (meleeReceiver == null)
        {
            meleeReceiver = GetComponent<NGH_Damageable>();
        }
    }

    private void OnEnable()
    {
        health.Died += HandleDied;
        health.Revived += HandleRevived;
    }

    private void OnDisable()
    {
        health.Died -= HandleDied;
        health.Revived -= HandleRevived;
    }

    private void Start()
    {
        if (meleeReceiver != null)
        {
            meleeReceiver.maxHp = receiverHp;
            meleeReceiver.hp = receiverHp;
        }
    }

    private void LateUpdate()
    {
        if (meleeReceiver == null || meleeReceiver.hp >= receiverHp)
        {
            return;
        }

        float lost = receiverHp - meleeReceiver.hp;
        meleeReceiver.hp = receiverHp;
        if (!health.IsAlive)
        {
            return;
        }

        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            player = found != null ? found.transform : null;
        }
        health.TakeDamage(lost, player != null ? player.position : transform.position);
    }

    private void HandleDied()
    {
        if (meleeReceiver != null)
        {
            meleeReceiver.SetLocked(false);
            meleeReceiver.enabled = false;
        }
    }

    private void HandleRevived()
    {
        if (meleeReceiver != null)
        {
            meleeReceiver.hp = receiverHp;
            meleeReceiver.enabled = true;
        }
    }
}
