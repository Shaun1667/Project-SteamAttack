using UnityEngine;

/// <summary>
/// 플레이어 피격 범위. 적의 공격 판정(OverlapSphere, Trigger 등)이 이 콜라이더에 닿으면
///   var hurt = col.GetComponent&lt;chg_PlayerHurtbox&gt;();  if (hurt) hurt.Hit(1, 공격위치);   // 특수 공격은 2
/// 처럼 호출하면 됩니다. 무적 중이면 false를 돌려줍니다.
/// </summary>
[RequireComponent(typeof(CapsuleCollider))]
public class chg_PlayerHurtbox : MonoBehaviour
{
    public chg_PlayerController player;

    void Awake()
    {
        if (!player) player = GetComponentInParent<chg_PlayerController>();
        var col = GetComponent<CapsuleCollider>();
        col.isTrigger = true;
    }

    public bool Hit(int damage, Vector3 from) => player && player.TakeHit(damage, from);
}
