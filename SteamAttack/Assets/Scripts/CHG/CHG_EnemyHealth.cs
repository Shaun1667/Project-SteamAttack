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
/// - 약점 노출(ExposeWeakness): 노출 중 다음 공격 1회의 피해가 Weakness Multiplier배가 되고, 그 공격이 맞으면 해제된다.
///   제한 시간이 지나도 해제된다. (패링 성공 시 CHG_EnemyAI가 호출)
///   약점 노출 중에는 락온 표시(NGH_Damageable이 만드는 chg_LockMarker 구)를 노랗게 빛내고 맥박처럼 키웠다 줄인다. (NGH 코드는 수정하지 않음)
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

    [Header("약점 노출")]
    [Tooltip("약점 노출 중 다음 공격 1회에 곱하는 배율")]
    [SerializeField, Min(1f)] private float weaknessMultiplier = 3f;
    [Tooltip("약점 노출 유지 시간(초). 그 전에 공격을 맞으면 바로 해제")]
    [SerializeField, Min(0.1f)] private float weaknessDuration = 4f;

    [Header("Debug")]
    [SerializeField] private bool logDamage = true;

    public float MaxHp => maxHp;
    public float CurrentHp => hp;
    public bool IsAlive => alive && hp > 0f && isActiveAndEnabled;
    public bool IsWeaknessExposed => weaknessTimer > 0f;

    /// <summary>(피해량, 공격 위치) — 살아 있을 때 피해를 받은 직후 호출</summary>
    public event Action<float, Vector3> Damaged;
    /// <summary>(맞은 적, 피해량) — 어떤 적이든 피해를 받으면 호출 (개발용 데미지 숫자 표시용)</summary>
    public static event Action<CHG_EnemyHealth, float> AnyDamaged;
    /// <summary>HP가 0이 된 순간 한 번 호출</summary>
    public event Action Died;
    /// <summary>부활하거나 풀에서 다시 꺼내져 HP가 다시 찼을 때 호출</summary>
    public event Action Revived;
    /// <summary>약점 노출 상태가 바뀔 때 (true = 노출 시작, false = 해제)</summary>
    public event Action<bool> WeaknessChanged;

    private float hp;
    private bool alive = true;
    private CHG_EnemyAI ai;
    private Rigidbody rb;
    private bool rbWasKinematic;
    private Renderer[] renderers;
    private Collider[] colliders;
    private Color[] baseColors;
    private Coroutine flashRoutine;
    private float weaknessTimer;

    // 약점 노출 표시 (락온 표시 구)
    private const string LockMarkerName = "chg_LockMarker";
    private static readonly Color WeakMarkerColor = new Color(1f, 0.85f, 0.1f, 1f);
    private Renderer lockMarker;
    private Color lockMarkerBaseColor;
    private Vector3 lockMarkerBaseScale;
    private bool lockMarkerTinted;

    private void Awake()
    {
        ai = GetComponent<CHG_EnemyAI>();
        rb = GetComponent<Rigidbody>();
        rbWasKinematic = rb != null && rb.isKinematic;
        // 락온 표시 구는 깜빡임·숨김 대상에서 제외 (색은 약점 표시가 따로 다룸)
        var list = new System.Collections.Generic.List<Renderer>(GetComponentsInChildren<Renderer>(true));
        list.RemoveAll(r => r == null || r.gameObject.name == LockMarkerName);
        renderers = list.ToArray();
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

        bool weakHit = IsWeaknessExposed;
        if (weakHit)
        {
            amount *= weaknessMultiplier;
            EndWeakness();
        }

        hp = Mathf.Max(0f, hp - amount);
        if (logDamage)
        {
            Debug.Log("[CHG_EnemyHealth:" + name + "] 피격 -" + amount + (weakHit ? " (약점 " + weaknessMultiplier + "배)" : "") + " (HP " + hp + "/" + maxHp + ")", this);
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
        AnyDamaged?.Invoke(this, amount);

        if (hp <= 0f)
        {
            Die();
        }
    }

    /// <summary>약점을 노출한다. duration이 0 이하면 Weakness Duration을 쓴다.</summary>
    public void ExposeWeakness(float duration = -1f)
    {
        if (!IsAlive)
        {
            return;
        }
        bool wasExposed = IsWeaknessExposed;
        weaknessTimer = duration > 0f ? duration : weaknessDuration;
        if (logDamage)
        {
            Debug.Log("[CHG_EnemyHealth:" + name + "] 약점 노출 " + weaknessTimer + "초 (다음 공격 " + weaknessMultiplier + "배)", this);
        }
        if (!wasExposed)
        {
            WeaknessChanged?.Invoke(true);
            SetMarkerTint(true);
        }
    }

    private void EndWeakness()
    {
        if (!IsWeaknessExposed)
        {
            return;
        }
        weaknessTimer = 0f;
        WeaknessChanged?.Invoke(false);
        SetMarkerTint(false);
    }

    private void Update()
    {
        // 약점 노출 중 락온 표시 맥박
        if (lockMarkerTinted && lockMarker != null)
        {
            float pulse = 1f + 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.time * 10f));
            lockMarker.transform.localScale = lockMarkerBaseScale * pulse;
        }

        if (weaknessTimer > 0f)
        {
            weaknessTimer -= Time.deltaTime;
            if (weaknessTimer <= 0f)
            {
                weaknessTimer = 0.0001f;   // EndWeakness가 이벤트를 보내도록
                EndWeakness();
                if (logDamage)
                {
                    Debug.Log("[CHG_EnemyHealth:" + name + "] 약점 노출 시간 종료", this);
                }
            }
        }
    }

    // 락온 표시 구를 노랗게 빛나게 / 원래대로
    private void SetMarkerTint(bool on)
    {
        if (on)
        {
            if (lockMarker == null)
            {
                Transform t = transform.Find(LockMarkerName);
                lockMarker = t != null ? t.GetComponent<Renderer>() : null;
            }
            if (lockMarker == null || lockMarkerTinted)
            {
                return;
            }
            Material m = lockMarker.material;
            lockMarkerBaseColor = m.color;
            lockMarkerBaseScale = lockMarker.transform.localScale;
            m.color = WeakMarkerColor;
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", WeakMarkerColor * 2.5f);
            }
            lockMarkerTinted = true;
        }
        else
        {
            if (lockMarker == null || !lockMarkerTinted)
            {
                return;
            }
            Material m = lockMarker.material;
            m.color = lockMarkerBaseColor;
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
            }
            lockMarker.transform.localScale = lockMarkerBaseScale;
            lockMarkerTinted = false;
        }
    }

    /// <summary>즉시 HP를 채우고 스폰 지점에서 다시 움직이게 한다.</summary>
    public void Revive()
    {
        StopAllCoroutines();
        flashRoutine = null;
        EndWeakness();
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
        EndWeakness();
        flashRoutine = null;
        RestoreColors();
    }

    private void Die()
    {
        alive = false;
        EndWeakness();
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
