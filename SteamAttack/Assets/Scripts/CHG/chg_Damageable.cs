using System.Collections;
using UnityEngine;

/// <summary>맞을 수 있는 대상(허수아비 등). 락온 대상이 되기도 합니다. 쓰러지면 일정 시간 뒤 부활합니다.
/// chg_ObjectPool 로 꺼내 쓰는 적에 붙여도 재사용 시 HP·표시가 초기화됩니다 (chg_IPoolable).</summary>
public class chg_Damageable : MonoBehaviour, chg_IPoolable
{
    public float maxHp = 5f;
    public float hp;
    [Tooltip("쓰러진 뒤 부활까지 걸리는 시간(초)")]
    public float respawnDelay = 5f;
    [Tooltip("락온 시 표시 위치(머리 위)")]
    public float markerHeight = 1.3f;

    Renderer[] _renderers;
    Color[] _baseColors;
    Collider[] _colliders;
    GameObject _marker;
    Coroutine _flash;
    bool _alive = true;

    void Awake()
    {
        hp = maxHp;
        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _baseColors[i] = _renderers[i].material.color;

        // 락온 표시용 작은 구 (UI 대신 월드 오브젝트) — 오브젝트 풀에서 꺼냄
        _marker = chg_ObjectPool.Spawn("chg_LockMarker", CreateMarker, transform);
        if (_marker)
        {
            _marker.transform.localPosition = Vector3.up * markerHeight / Mathf.Max(0.01f, transform.lossyScale.y);
            _marker.transform.localScale = Vector3.one * 0.12f / Mathf.Max(0.01f, transform.lossyScale.x);
            _marker.SetActive(false);
        }
    }

    // 풀이 비었을 때만 호출됨
    static GameObject CreateMarker()
    {
        var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(m.GetComponent<Collider>());   // 판정에 걸리지 않게 바로 제거
        m.GetComponent<Renderer>().material.color = Color.red;
        return m;
    }

    // ── 오브젝트 풀 재사용 시 초기화 ──
    public void OnSpawned()
    {
        StopAllCoroutines();
        _flash = null;
        hp = maxHp;
        _alive = true;
        for (int i = 0; i < _renderers.Length; i++) if (_renderers[i]) _renderers[i].material.color = _baseColors[i];
        SetVisible(true);
        SetLocked(false);
    }

    public void OnDespawned()
    {
        StopAllCoroutines();
        SetLocked(false);
    }

    public Vector3 AimPoint => transform.position + Vector3.up * markerHeight * 0.6f;
    public bool IsAlive => _alive && hp > 0f && isActiveAndEnabled;   // 풀로 반납(비활성)되면 락온 해제

    public void SetLocked(bool locked)
    {
        if (_marker) _marker.SetActive(locked && IsAlive);
    }

    public void TakeDamage(float amount, Vector3 from)
    {
        if (!IsAlive) return;
        hp -= amount;
        Debug.Log($"[CHG] {name} 피격 -{amount} (남은 HP {Mathf.Max(0, hp)}/{maxHp})");
        if (_flash != null) StopCoroutine(_flash);
        _flash = StartCoroutine(Flash());

        // 살짝 밀려남
        Vector3 push = transform.position - from; push.y = 0;
        if (push.sqrMagnitude > 0.0001f) transform.position += push.normalized * 0.08f;

        if (hp <= 0f) StartCoroutine(DieAndRespawn());
    }

    IEnumerator Flash()
    {
        for (int i = 0; i < _renderers.Length; i++) _renderers[i].material.color = Color.white;
        yield return new WaitForSeconds(0.08f);
        for (int i = 0; i < _renderers.Length; i++) _renderers[i].material.color = _baseColors[i];
    }

    IEnumerator DieAndRespawn()
    {
        _alive = false;
        Debug.Log($"[CHG] {name} 쓰러짐 → {respawnDelay:0.#}초 후 부활");
        yield return new WaitForSeconds(0.1f);            // 마지막 깜빡임은 보여 주고
        SetVisible(false);
        if (_marker) _marker.SetActive(false);
        yield return new WaitForSeconds(respawnDelay);
        hp = maxHp;
        _alive = true;
        SetVisible(true);
        Debug.Log($"[CHG] {name} 부활 (HP {maxHp})");
    }

    void SetVisible(bool v)
    {
        foreach (var r in _renderers) if (r) r.enabled = v;
        foreach (var c in _colliders) if (c) c.enabled = v;
    }
}
