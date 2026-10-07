using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 적 HP와 피해 처리 (NGH)
/// - 다른 파트(YPH 총·수류탄, CHG 근접)가 공통으로 쓰는 피해 API: IsAlive, TakeDamage(float, Vector3)
/// - HP가 0이 되면 같은 오브젝트의 CHG_EnemyAI를 멈추고, 잠시 뒤 몸(렌더러·콜라이더)을 숨긴다.
///   피해를 준 호출 안에서 오브젝트를 파괴하거나 끄지 않으므로, 공격한 쪽은 명중 직후 태그를 읽을 수 있다.
/// - Respawn Delay가 0보다 크면 그 시간 뒤 스폰 지점에서 부활한다.
/// - 오브젝트 풀(CHG_ObjectPool)로 다시 꺼낼 때도 HP·생존 상태가 초기화된다.
/// </summary>
[DisallowMultipleComponent]
public class CHG_EnemyHealth : MonoBehaviour, CHG_IPoolable
{
    [Header("HP")]
    [SerializeField, Min(1f)] private float maxHp = 5f;
    [Tooltip("맞았을 때 하얗게 깜빡이는 시간(초). 0이면 깜빡이지 않음")]
    [SerializeField, Min(0f)] private float hitFlashTime = 0.08f;

    [Header("사망 / 부활")]
    [Tooltip("쓰러진 뒤 몸을 숨기기까지의 시간(초)")]
    [SerializeField, Min(0f)] private float hideDelay = 0.15f;
    [Tooltip("쓰러진 뒤 부활까지의 시간(초). 0 이하면 부활하지 않음")]
    [SerializeField] private float respawnDelay = 5f;

    [Header("Debug")]
    [SerializeField] private bool logDamage = true;

    public float MaxHp => maxHp;
    public float CurrentHp => hp;
    public bool IsAlive => alive && hp > 0f && isActiveAndEnabled;

    /// <summary>(피해량, 공격 위치) — 살아 있을 때 피해를 받은 직후 호출</summary>
    public event Action<float, Vector3> Damaged;
    /// <summary>HP가 0이 된 순간 한 번 호출</summary>
    public event Action Died;
    /// <summary>부활하거나 풀에서 다시 꺼내져 HP가 다시 찼을 때 호출</summary>
    public event Action Revived;

    private float hp;
    private bool alive = true;
    private CHG_EnemyAI ai;
    private Rigidbody rb;
    private bool rbWasKinematic;
    private Renderer[] renderers;
    private Collider[] colliders;
    private Color[] baseColors;
    private Coroutine flashRoutine;

    private void Awake()
    {
        ai = GetComponent<CHG_EnemyAI>();
        rb = GetComponent<Rigidbody>();
        rbWasKinematic = rb != null && rb.isKinematic;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            baseColors[i] = renderers[i].material.color;
        }
        hp = maxHp;
    }

    /// <summary>피해를 준다. 죽어 있거나 0 이하의 피해는 무시한다.</summary>
    public void TakeDamage(float amount, Vector3 hitFrom)
    {
        if (!IsAlive || amount <= 0f)
        {
            return;
        }

        hp = Mathf.Max(0f, hp - amount);
        if (logDamage)
        {
            Debug.Log("[CHG_EnemyHealth:" + name + "] 피격 -" + amount + " (HP " + hp + "/" + maxHp + ")", this);
        }

        if (hitFlashTime > 0f && hp > 0f)
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
            }
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        Damaged?.Invoke(amount, hitFrom);

        if (hp <= 0f)
        {
            Die();
        }
    }

    /// <summary>즉시 HP를 채우고 스폰 지점에서 다시 움직이게 한다.</summary>
    public void Revive()
    {
        StopAllCoroutines();
        flashRoutine = null;
        hp = maxHp;
        alive = true;
        RestoreColors();
        SetBodyActive(true);
        if (ai != null)
        {
            ai.ReviveAtHome();
        }
        if (logDamage)
        {
            Debug.Log("[CHG_EnemyHealth:" + name + "] 부활 (HP " + maxHp + ")", this);
        }
        Revived?.Invoke();
    }

    public void OnSpawned()
    {
        Revive();
    }

    public void OnDespawned()
    {
        StopAllCoroutines();
        flashRoutine = null;
        RestoreColors();
    }

    private void Die()
    {
        alive = false;
        if (logDamage)
        {
            Debug.Log("[CHG_EnemyHealth:" + name + "] 쓰러짐" + (respawnDelay > 0f ? " → " + respawnDelay + "초 후 부활" : ""), this);
        }
        if (ai != null)
        {
            ai.StopForDeath();
        }
        Died?.Invoke();
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        if (hideDelay > 0f)
        {
            yield return new WaitForSeconds(hideDelay);
        }
        RestoreColors();
        SetBodyActive(false);

        if (respawnDelay <= 0f)
        {
            yield break;
        }
        yield return new WaitForSeconds(respawnDelay);
        Revive();
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].material.color = Color.white;
        }
        yield return new WaitForSeconds(hitFlashTime);
        RestoreColors();
        flashRoutine = null;
    }

    private void RestoreColors()
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].material.color = baseColors[i];
        }
    }

    private void SetBodyActive(bool on)
    {
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = on;
        }
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = on;
        }
        if (rb != null)
        {
            if (!on)
            {
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = rbWasKinematic;
            }
        }
    }
}
