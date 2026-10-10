using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 포탈 도착 지점 (NGH)
    /// - NGH_ScenePortal로 이 씬에 왔고 그 포탈의 targetSpawnId가 이 오브젝트의 spawnId와 같으면,
    ///   씬 시작 시 플레이어(태그 Player)를 이 위치·방향으로 옮긴다.
    /// - 포탈 없이 씬을 바로 실행하면 아무것도 하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PortalSpawnPoint : MonoBehaviour
    {
        [SerializeField] private string spawnId;
        [SerializeField] private string playerTag = "Player";

        public string SpawnId => spawnId;

        private void Start()
        {
            if (string.IsNullOrEmpty(spawnId) || ScenePortal.PendingSpawnId != spawnId) return;
            ScenePortal.PendingSpawnId = null;

            GameObject p = GameObject.FindGameObjectWithTag(playerTag);
            if (p == null)
            {
                Debug.LogWarning($"[NGH_PortalSpawnPoint] 태그 {playerTag} 오브젝트를 찾지 못했습니다.", this);
                return;
            }

            // CharacterController는 꺼야 순간이동이 적용된다
            CharacterController cc = p.GetComponent<CharacterController>();
            bool ccEnabled = cc && cc.enabled;
            if (ccEnabled) cc.enabled = false;
            p.transform.SetPositionAndRotation(transform.position, transform.rotation);
            if (ccEnabled) cc.enabled = true;
            Physics.SyncTransforms();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward * 1.2f);
        }
    }
}
