using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오브젝트 풀. 런타임에 만드는 모든 오브젝트는 Instantiate/Destroy 대신 이걸 씁니다.
///
///   생성 : var fx = NGH_ObjectPool.Spawn(hitFxPrefab, pos, rot);          // Instantiate 대신
///          var bullet = NGH_ObjectPool.Spawn(bulletPrefab, pos, rot);       // 컴포넌트 프리팹도 가능 (제네릭)
///   반납 : NGH_ObjectPool.Despawn(fx);            // Destroy 대신
///          NGH_ObjectPool.Despawn(fx, 1.5f);      // Destroy(obj, t) 대신 — 1.5초 뒤 반납
///   예열 : NGH_ObjectPool.Prewarm(hitFxPrefab, 10);  // 로딩 때 미리 만들어 두기 (Inspector 의 Prewarm 목록도 가능)
///   코드로 만드는 오브젝트(프리미티브 등) : NGH_ObjectPool.Spawn("키", () => 만드는함수(), parent);
///
/// 씬에 따로 배치하지 않아도 처음 Spawn 할 때 "NGH_ObjectPool" 오브젝트가 자동으로 생깁니다.
/// 예열 목록·최대 개수를 정하고 싶으면 씬에 빈 오브젝트를 만들고 이 컴포넌트를 붙이세요.
/// 재사용될 때 초기화가 필요한 스크립트는 NGH_IPoolable 을 구현하세요 (Awake 는 처음 한 번만 실행됨).
/// 풀은 씬에 속합니다 — 씬이 바뀌면 풀과 꺼내진 오브젝트가 함께 정리됩니다.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class NGH_ObjectPool : MonoBehaviour
{
    [Serializable]
    public class PrewarmEntry
    {
        public GameObject prefab;
        [Min(0)] public int count = 5;
    }

    [Tooltip("시작할 때 미리 만들어 둘 프리팹과 개수 (첫 생성 때 끊김 방지)")]
    public List<PrewarmEntry> prewarm = new List<PrewarmEntry>();
    [Tooltip("풀 하나에 보관할 최대 개수. 넘치게 반납되면 그 오브젝트는 파괴 (0 = 제한 없음)")]
    [Min(0)] public int maxPerPool = 0;
    [Tooltip("새로 만들 때마다 Console 에 로그 (풀 크기 확인용)")]
    public bool logCreate = false;

    static NGH_ObjectPool _instance;
    static bool _quitting;

    readonly Dictionary<object, Stack<GameObject>> _free = new Dictionary<object, Stack<GameObject>>();
    readonly Dictionary<object, int> _createdCount = new Dictionary<object, int>();
    Transform _storage;   // 비활성 보관함 — 이 아래에서 만들어지면 꺼낼 때까지 Awake 가 실행되지 않음
    static readonly List<NGH_IPoolable> _callbackBuffer = new List<NGH_IPoolable>();

    // Enter Play Mode 옵션으로 도메인 리로드를 꺼도 정적 값이 남지 않게
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _instance = null; _quitting = false; }

    /// <summary>현재 풀. 없으면 자동 생성 (게임 종료 중에는 null)</summary>
    public static NGH_ObjectPool Instance
    {
        get
        {
            if (_instance || _quitting) return _instance;
            _instance = FindAnyObjectByType<NGH_ObjectPool>();
            if (!_instance) _instance = new GameObject("NGH_ObjectPool").AddComponent<NGH_ObjectPool>();
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance && _instance != this)
        {
            Debug.LogWarning("[CHG] NGH_ObjectPool 이 씬에 두 개 있습니다. 나중 것은 무시합니다.", this);
            enabled = false;
            return;
        }
        _instance = this;
        EnsureStorage();
        Application.quitting += OnQuitting;
        foreach (var e in prewarm)
            if (e != null && e.prefab) Prewarm(e.prefab, e.count);
    }

    void OnDestroy()
    {
        Application.quitting -= OnQuitting;
        if (_instance == this) _instance = null;
    }

    static void OnQuitting() { _quitting = true; }

    void EnsureStorage()
    {
        if (_storage) return;
        var s = new GameObject("chg_PoolStorage (inactive)");
        s.SetActive(false);
        s.transform.SetParent(transform, false);
        _storage = s.transform;
    }

    // ───────────────────────── 꺼내기 ─────────────────────────

    /// <summary>프리팹을 풀에서 꺼냄 (없으면 새로 만듦). Instantiate(prefab, pos, rot, parent) 대신 사용.</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (!prefab) { Debug.LogError("[CHG] NGH_ObjectPool.Spawn: prefab 이 비어 있습니다."); return null; }
        var pool = Instance;
        if (!pool) return null;
        var go = pool.Take(prefab, prefab.name, () => Instantiate(prefab, pool._storage));
        return pool.Activate(go, parent, true, position, rotation);
    }

    /// <summary>프리팹의 위치·회전 그대로 꺼냄. parent 를 주면 그 아래 프리팹의 로컬 위치로 붙음.</summary>
    public static GameObject Spawn(GameObject prefab, Transform parent = null)
    {
        if (!prefab) { Debug.LogError("[CHG] NGH_ObjectPool.Spawn: prefab 이 비어 있습니다."); return null; }
        var pool = Instance;
        if (!pool) return null;
        var go = pool.Take(prefab, prefab.name, () => Instantiate(prefab, pool._storage));
        var t = prefab.transform;
        go = pool.Activate(go, parent, false, t.localPosition, t.localRotation);
        return go;
    }

    /// <summary>컴포넌트 프리팹 버전 — 꺼낸 오브젝트의 같은 컴포넌트를 돌려줌</summary>
    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
    {
        if (!prefab) { Debug.LogError("[CHG] NGH_ObjectPool.Spawn: prefab 이 비어 있습니다."); return null; }
        var go = Spawn(prefab.gameObject, position, rotation, parent);
        return go ? go.GetComponent<T>() : null;
    }

    /// <summary>
    /// 프리팹 없이 코드로 만드는 오브젝트용 (CreatePrimitive, new GameObject 등).
    /// 같은 key 끼리 재사용됩니다. factory 는 풀이 비었을 때만 호출됩니다. parent 아래 로컬 (0,0,0) 에 붙습니다.
    /// </summary>
    public static GameObject Spawn(string key, Func<GameObject> factory, Transform parent = null)
    {
        if (string.IsNullOrEmpty(key) || factory == null) { Debug.LogError("[CHG] NGH_ObjectPool.Spawn: key/factory 가 비어 있습니다."); return null; }
        var pool = Instance;
        if (!pool) return null;
        var go = pool.Take(key, key, () =>
        {
            var made = factory();
            if (made) made.transform.SetParent(pool._storage, false);
            return made;
        });
        return pool.Activate(go, parent, false, Vector3.zero, Quaternion.identity);
    }

    /// <summary>로딩 시점에 미리 만들어서 풀에 넣어 둠 (이미 보관 중인 개수 포함해 count 개가 되게)</summary>
    public static void Prewarm(GameObject prefab, int count)
    {
        if (!prefab || count <= 0) return;
        var pool = Instance;
        if (!pool) return;
        pool.EnsureStorage();
        var stack = pool.GetStack(prefab);
        while (stack.Count < count)
        {
            var go = pool.Create(prefab, prefab.name, () => Instantiate(prefab, pool._storage));
            if (!go) break;
            go.SetActive(false);
            go.GetComponent<NGH_PooledObject>().inPool = true;
            stack.Push(go);
        }
    }

    // ───────────────────────── 반납 ─────────────────────────

    /// <summary>풀로 반납. Destroy(go) 대신 사용. 풀 출신이 아니면 그냥 파괴합니다.</summary>
    public static void Despawn(GameObject go, float delay = 0f)
    {
        if (!go) return;
        var po = go.GetComponent<NGH_PooledObject>();
        if (!po || !_instance)
        {
            if (!po) Debug.LogWarning($"[CHG] {go.name} 은 풀에서 만든 오브젝트가 아니라서 파괴합니다. NGH_ObjectPool.Spawn 으로 만들어 주세요.", go);
            if (delay > 0f) Destroy(go, delay); else Destroy(go);
            return;
        }
        if (po.inPool) return;   // 이미 반납됨
        if (delay > 0f) { _instance.StartCoroutine(_instance.DespawnLater(po, po.spawnVersion, delay)); return; }
        _instance.Return(po);
    }

    /// <summary>컴포넌트로 반납</summary>
    public static void Despawn(Component c, float delay = 0f) { if (c) Despawn(c.gameObject, delay); }

    /// <summary>해당 프리팹 풀에 대기 중인 개수 (디버그용)</summary>
    public static int FreeCount(GameObject prefab)
    {
        Stack<GameObject> s;
        return _instance && prefab && _instance._free.TryGetValue(prefab, out s) ? s.Count : 0;
    }

    // ───────────────────────── 내부 ─────────────────────────

    Stack<GameObject> GetStack(object key)
    {
        Stack<GameObject> s;
        if (!_free.TryGetValue(key, out s)) { s = new Stack<GameObject>(); _free[key] = s; }
        return s;
    }

    GameObject Take(object key, string label, Func<GameObject> create)
    {
        EnsureStorage();
        var stack = GetStack(key);
        while (stack.Count > 0)
        {
            var g = stack.Pop();
            if (g) return g;   // 씬 정리 등으로 파괴된 항목은 건너뜀
        }
        return Create(key, label, create);
    }

    GameObject Create(object key, string label, Func<GameObject> create)
    {
        var go = create();
        if (!go) return null;
        int n;
        _createdCount.TryGetValue(key, out n);
        _createdCount[key] = ++n;
        go.name = label;
        var po = go.GetComponent<NGH_PooledObject>();
        if (!po) po = go.AddComponent<NGH_PooledObject>();
        po.poolKey = key;
        po.inPool = false;
        if (logCreate) Debug.Log($"[CHG][Pool] {label} 새로 생성 (총 {n}개)");
        return go;
    }

    GameObject Activate(GameObject go, Transform parent, bool world, Vector3 pos, Quaternion rot)
    {
        if (!go) return null;
        var po = go.GetComponent<NGH_PooledObject>();
        po.inPool = false;
        po.spawnVersion++;

        var t = go.transform;
        t.SetParent(parent, false);
        if (world) t.SetPositionAndRotation(pos, rot);
        else { t.localPosition = pos; t.localRotation = rot; }
        go.SetActive(true);   // 처음 꺼낸 경우 여기서 Awake/OnEnable 실행

        go.GetComponentsInChildren(true, _callbackBuffer);
        for (int i = 0; i < _callbackBuffer.Count; i++) _callbackBuffer[i].OnSpawned();
        _callbackBuffer.Clear();
        return go;
    }

    void Return(NGH_PooledObject po)
    {
        var go = po.gameObject;
        go.GetComponentsInChildren(true, _callbackBuffer);
        for (int i = 0; i < _callbackBuffer.Count; i++) _callbackBuffer[i].OnDespawned();
        _callbackBuffer.Clear();

        po.inPool = true;
        po.spawnVersion++;
        var stack = GetStack(po.poolKey);
        if (maxPerPool > 0 && stack.Count >= maxPerPool) { Destroy(go); return; }

        go.SetActive(false);
        EnsureStorage();
        go.transform.SetParent(_storage, false);
        stack.Push(go);
    }

    IEnumerator DespawnLater(NGH_PooledObject po, int version, float delay)
    {
        yield return new WaitForSeconds(delay);
        // 그 사이 이미 반납됐다가 다시 꺼내졌으면 이번 예약은 취소
        if (po && !po.inPool && po.spawnVersion == version) Return(po);
    }
}
