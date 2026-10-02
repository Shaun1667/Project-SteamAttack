using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 풀링 오브젝트가 꺼내질 때(Spawn) / 반납될 때(Despawn) 알림을 받고 싶으면 구현한다.
/// 풀링 오브젝트는 Awake/Start가 최초 1회만 호출되므로,
/// 꺼낼 때마다 초기화해야 하는 값은 OnSpawned에서 리셋한다.
/// </summary>
public interface NGH_IPoolable
{
    void OnSpawned();
    void OnDespawned();
}

/// <summary>
/// 프리팹별 오브젝트 풀 (NGH)
/// - Instantiate 대신 NGH_ObjectPool.Spawn, Destroy 대신 NGH_ObjectPool.Despawn 을 사용한다.
/// - 씬에 따로 배치하지 않아도 처음 사용할 때 [NGH_ObjectPool] 오브젝트가 자동 생성된다.
/// - DontDestroyOnLoad 로 씬이 바뀌어도 풀(비활성 오브젝트)이 유지된다.
/// - 씬이 언로드되면, 부모 없이 Spawn 해서 아직 사용 중인 오브젝트는 자동 반납된다.
///
/// 사용 예)
///   Bullet b = NGH_ObjectPool.Spawn(bulletPrefab, pos, rot);
///   NGH_ObjectPool.Despawn(b.gameObject);          // 즉시 반납
///   NGH_ObjectPool.Despawn(b.gameObject, 2f);      // 2초 뒤 반납
///   NGH_ObjectPool.Prewarm(bulletPrefab.gameObject, 10); // 미리 10개 만들어 두기
/// </summary>
[DisallowMultipleComponent]
public class NGH_ObjectPool : MonoBehaviour
{
    private class Pool
    {
        public GameObject Prefab;
        public Transform Root;
        public readonly Stack<GameObject> Inactive = new Stack<GameObject>();
        public int TotalCreated;
    }

    private static NGH_ObjectPool instance;
    private static bool isQuitting;

    private readonly Dictionary<GameObject, Pool> poolsByPrefab = new Dictionary<GameObject, Pool>();
    private readonly Dictionary<GameObject, Pool> poolsByInstance = new Dictionary<GameObject, Pool>();
    private readonly Dictionary<GameObject, int> spawnVersions = new Dictionary<GameObject, int>();
    private readonly HashSet<GameObject> inactiveSet = new HashSet<GameObject>();

    // 생성 직후 Awake가 바로 호출되지 않도록 비활성 상태로 두는 보관용 부모
    private Transform storage;

    public static NGH_ObjectPool Instance
    {
        get
        {
            if (instance == null && !isQuitting)
            {
                instance = FindAnyObjectByType<NGH_ObjectPool>();
                if (instance == null)
                {
                    instance = new GameObject("[NGH_ObjectPool]").AddComponent<NGH_ObjectPool>();
                }
            }
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isQuitting = false;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        // 씬이 바뀌어도 풀을 유지한다 (DontDestroyOnLoad는 루트 오브젝트만 가능)
        if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneUnloaded += OnSceneUnloaded;
        EnsureStorage();
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;

        if (instance == this)
        {
            instance = null;
        }
    }

    #region Static API

    /// <summary>풀에서 오브젝트를 꺼낸다. 남는 게 없으면 새로 만든다.</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
        {
            Debug.LogError("[NGH_ObjectPool] Spawn 실패: prefab이 비어 있습니다.");
            return null;
        }

        NGH_ObjectPool pool = Instance;
        return pool != null ? pool.SpawnInternal(prefab, position, rotation, parent) : null;
    }

    /// <summary>컴포넌트 타입으로 바로 받는 Spawn.</summary>
    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
    {
        if (prefab == null)
        {
            Debug.LogError("[NGH_ObjectPool] Spawn 실패: prefab이 비어 있습니다.");
            return null;
        }

        GameObject spawned = Spawn(prefab.gameObject, position, rotation, parent);
        return spawned != null ? spawned.GetComponent<T>() : null;
    }

    /// <summary>오브젝트를 풀에 반납한다.</summary>
    public static void Despawn(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        if (instance == null)
        {
            Destroy(obj);
            return;
        }
        instance.DespawnInternal(obj);
    }

    /// <summary>delay초 뒤에 풀에 반납한다. 그 전에 반납/재사용되면 이 예약은 무시된다.</summary>
    public static void Despawn(GameObject obj, float delay)
    {
        if (obj == null)
        {
            return;
        }

        if (delay <= 0f)
        {
            Despawn(obj);
            return;
        }

        if (instance == null)
        {
            Destroy(obj, delay);
            return;
        }
        instance.StartCoroutine(instance.DespawnAfter(obj, delay));
    }

    /// <summary>비활성 상태로 최소 count개를 미리 만들어 둔다.</summary>
    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
        {
            return;
        }

        NGH_ObjectPool pool = Instance;
        if (pool != null)
        {
            pool.PrewarmInternal(prefab, count);
        }
    }

    #endregion

    #region Internal

    // 씬이 언로드될 때: 이전 씬에서 부모 없이 꺼내 쓰던(=풀 루트 아래에 있는) 활성 오브젝트를 반납하고,
    // 씬과 함께 파괴된 오브젝트의 기록을 정리한다.
    private void OnSceneUnloaded(Scene scene)
    {
        List<GameObject> keys = new List<GameObject>(poolsByInstance.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            GameObject obj = keys[i];

            if (obj == null)
            {
                poolsByInstance.Remove(obj);
                spawnVersions.Remove(obj);
                inactiveSet.Remove(obj);
                continue;
            }

            Pool pool = poolsByInstance[obj];
            if (!inactiveSet.Contains(obj) && obj.transform.parent == pool.Root)
            {
                DespawnInternal(obj);
            }
        }
    }

    private void EnsureStorage()
    {
        if (storage != null)
        {
            return;
        }

        GameObject storageObject = new GameObject("Storage");
        storageObject.SetActive(false);
        storage = storageObject.transform;
        storage.SetParent(transform, false);
    }

    private Pool GetPool(GameObject prefab)
    {
        if (!poolsByPrefab.TryGetValue(prefab, out Pool pool))
        {
            Transform root = new GameObject(prefab.name + " Pool").transform;
            root.SetParent(transform, false);
            pool = new Pool { Prefab = prefab, Root = root };
            poolsByPrefab.Add(prefab, pool);
        }
        return pool;
    }

    private GameObject CreateInstance(Pool pool)
    {
        EnsureStorage();

        // 비활성 부모 아래에서 생성 → 처음 Spawn 될 때 Awake가 호출된다
        GameObject obj = Instantiate(pool.Prefab, storage, false);
        pool.TotalCreated++;
        obj.name = pool.Prefab.name + " (" + pool.TotalCreated + ")";
        obj.SetActive(false);
        obj.transform.SetParent(pool.Root, false);

        poolsByInstance[obj] = pool;
        spawnVersions[obj] = 0;
        return obj;
    }

    private GameObject SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        Pool pool = GetPool(prefab);

        GameObject obj = null;
        while (obj == null && pool.Inactive.Count > 0)
        {
            GameObject candidate = pool.Inactive.Pop();
            inactiveSet.Remove(candidate);

            if (candidate == null)
            {
                // 외부에서 Destroy 된 오브젝트는 정리
                poolsByInstance.Remove(candidate);
                spawnVersions.Remove(candidate);
                continue;
            }
            obj = candidate;
        }

        if (obj == null)
        {
            obj = CreateInstance(pool);
        }

        Transform objTransform = obj.transform;
        objTransform.SetParent(parent != null ? parent : pool.Root, false);
        objTransform.SetPositionAndRotation(position, rotation);

        spawnVersions[obj] = spawnVersions.TryGetValue(obj, out int version) ? version + 1 : 1;
        obj.SetActive(true);

        NGH_IPoolable[] poolables = obj.GetComponentsInChildren<NGH_IPoolable>(true);
        for (int i = 0; i < poolables.Length; i++)
        {
            poolables[i].OnSpawned();
        }

        return obj;
    }

    private void DespawnInternal(GameObject obj)
    {
        if (!poolsByInstance.TryGetValue(obj, out Pool pool))
        {
            Debug.LogWarning("[NGH_ObjectPool] 풀에서 생성되지 않은 오브젝트라 Destroy 합니다: " + obj.name, obj);
            Destroy(obj);
            return;
        }

        // 이미 반납된 오브젝트
        if (inactiveSet.Contains(obj))
        {
            return;
        }

        NGH_IPoolable[] poolables = obj.GetComponentsInChildren<NGH_IPoolable>(true);
        for (int i = 0; i < poolables.Length; i++)
        {
            poolables[i].OnDespawned();
        }

        // 버전을 올려서 대기 중인 지연 반납 예약을 무효화
        spawnVersions[obj] = spawnVersions.TryGetValue(obj, out int version) ? version + 1 : 1;

        obj.SetActive(false);
        obj.transform.SetParent(pool.Root, false);
        pool.Inactive.Push(obj);
        inactiveSet.Add(obj);
    }

    private IEnumerator DespawnAfter(GameObject obj, float delay)
    {
        bool isPooled = poolsByInstance.ContainsKey(obj);
        int version = spawnVersions.TryGetValue(obj, out int v) ? v : -1;

        yield return new WaitForSeconds(delay);

        if (obj == null)
        {
            yield break;
        }

        if (!isPooled)
        {
            DespawnInternal(obj);
            yield break;
        }

        if (spawnVersions.TryGetValue(obj, out int current) && current == version)
        {
            DespawnInternal(obj);
        }
    }

    private void PrewarmInternal(GameObject prefab, int count)
    {
        Pool pool = GetPool(prefab);
        int toCreate = count - pool.Inactive.Count;
        for (int i = 0; i < toCreate; i++)
        {
            GameObject obj = CreateInstance(pool);
            pool.Inactive.Push(obj);
            inactiveSet.Add(obj);
        }
    }

    #endregion
}
