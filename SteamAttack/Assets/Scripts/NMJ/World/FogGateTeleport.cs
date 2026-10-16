using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 안개문 — 같은 씬 안에서 플레이어를 다른 자리로 옮긴다 (현무월드 안 향원정 → 보스전 아레나).
    ///
    /// NGH ScenePortal은 다른 씬을 불러오는 포탈이라, 보스전 아레나를 현무월드(Main 씬) 안에 두면서 쓰려고 만든 것.
    /// - 플레이어(태그 Player)가 이 오브젝트의 콜라이더 영역에 들어오면 target 위치·방향으로 옮긴다.
    /// - 트리거 이벤트가 오지 않는 구성(CharacterController만 있는 경우)에 대비해 위치로도 확인한다.
    /// - 도착하자마자 되돌아가지 않도록, 옮긴 뒤 cooldown 동안은 모든 안개문이 쉰다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FogGateTeleport : MonoBehaviour
    {
        [Tooltip("플레이어를 옮길 자리 (위치·방향)")]
        [SerializeField] private Transform target;
        [SerializeField] private string playerTag = "Player";
        [Tooltip("옮긴 뒤 다시 발동하기까지의 시간(초). 모든 안개문이 함께 쉰다")]
        [SerializeField, Min(0f)] private float cooldown = 1.5f;
        [SerializeField] private bool logTeleport = true;

        private static float nextAllowedTime;
        private Collider area;
        private Transform player;

        // 플레이 시작 때 도메인을 다시 불러오지 않는 설정에서도 지난 플레이의 대기 시간이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => nextAllowedTime = 0f;

        public Transform Target { get => target; set => target = value; }

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        private void Awake()
        {
            area = GetComponent<Collider>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(playerTag) || other.transform.root.CompareTag(playerTag)) TryTeleport(other.transform);
        }

        private void Update()
        {
            if (player == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag(playerTag);
                if (p == null) return;
                player = p.transform;
            }
            if (area != null && area.bounds.Contains(player.position + Vector3.up * 0.5f)) TryTeleport(player);
        }

        private void TryTeleport(Transform who)
        {
            if (target == null || Time.time < nextAllowedTime) return;

            // 태그가 붙은 플레이어 본체를 옮긴다
            Transform body = who.CompareTag(playerTag) ? who : who.root;
            nextAllowedTime = Time.time + cooldown;

            // CharacterController는 꺼야 순간이동이 적용된다
            CharacterController cc = body.GetComponent<CharacterController>();
            bool ccEnabled = cc && cc.enabled;
            if (ccEnabled) cc.enabled = false;
            body.SetPositionAndRotation(target.position, target.rotation);
            if (ccEnabled) cc.enabled = true;
            Physics.SyncTransforms();

            if (logTeleport) Debug.Log($"[NMJ_FogGate] {name} → {target.name}", this);
        }

        private void OnDrawGizmos()
        {
            Collider c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(0.85f, 0.9f, 1f, 0.3f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
            if (target != null)
            {
                Gizmos.color = new Color(0.85f, 0.9f, 1f, 0.8f);
                Gizmos.DrawLine(c.bounds.center, target.position);
            }
        }
    }
}
