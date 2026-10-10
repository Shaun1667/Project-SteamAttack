using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 이동 포탈 (NGH)
/// - 플레이어(태그 Player)가 이 오브젝트의 트리거 영역에 들어오면 targetScene으로 이동한다.
/// - 도착 씬에 같은 ID의 NGH_PortalSpawnPoint가 있으면 그 위치에 플레이어를 놓는다.
/// - 이동할 씬은 Build Settings(File > Build Profiles)의 씬 목록에 등록되어 있어야 한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class NGH_ScenePortal : MonoBehaviour
{
    /// <summary>다음 씬에서 플레이어를 놓을 스폰 ID (씬 이동 사이에만 유지)</summary>
    public static string PendingSpawnId;

    [Tooltip("이동할 씬 이름 (확장자 없이). Build Settings에 등록되어 있어야 한다")]
    [SerializeField] private string targetScene;
    [Tooltip("도착 씬의 NGH_PortalSpawnPoint ID. 비우면 도착 씬의 기본 위치에서 시작")]
    [SerializeField] private string targetSpawnId;
    [SerializeField] private string playerTag = "Player";
    [Tooltip("씬이 시작된 뒤 이 시간(초) 동안은 발동하지 않는다 (도착하자마자 되돌아가는 것 방지)")]
    [SerializeField, Min(0f)] private float armDelay = 1f;
    [SerializeField] private bool logPortal = true;

    private Collider area;
    private Transform player;
    private float enabledTime;
    private bool loading;

    public string TargetScene => targetScene;
    public string TargetSpawnId => targetSpawnId;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c) c.isTrigger = true;
    }

    private void Awake()
    {
        area = GetComponent<Collider>();
    }

    private void OnEnable()
    {
        enabledTime = Time.time;
        loading = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other.transform)) TryEnter();
    }

    // 트리거 이벤트가 안 오는 구성(CharacterController만 있는 경우 등)에 대비해 위치로도 확인한다
    private void Update()
    {
        if (loading) return;
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag(playerTag);
            if (p == null) return;
            player = p.transform;
        }
        if (area != null && area.bounds.Contains(player.position + Vector3.up * 0.5f)) TryEnter();
    }

    private bool IsPlayer(Transform t)
    {
        return t != null && (t.CompareTag(playerTag) || t.root.CompareTag(playerTag));
    }

    private void TryEnter()
    {
        if (loading || Time.time - enabledTime < armDelay) return;
        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning("[NGH_ScenePortal] 이동할 씬 이름이 비어 있습니다.", this);
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            Debug.LogError($"[NGH_ScenePortal] 씬 '{targetScene}'이(가) Build Settings에 등록되어 있지 않습니다.", this);
            loading = true; // 같은 에러를 매 프레임 반복하지 않게
            return;
        }
        loading = true;
        PendingSpawnId = targetSpawnId;
        if (logPortal) Debug.Log($"[NGH_ScenePortal] {targetScene} 씬으로 이동 (스폰: {(string.IsNullOrEmpty(targetSpawnId) ? "기본" : targetSpawnId)})", this);
        SceneManager.LoadScene(targetScene);
    }

    private void OnDrawGizmos()
    {
        Collider c = GetComponent<Collider>();
        if (c == null) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.25f);
        Gizmos.DrawCube(c.bounds.center, c.bounds.size);
    }
}
