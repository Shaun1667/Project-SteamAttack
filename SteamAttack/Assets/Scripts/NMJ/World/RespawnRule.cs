using NGH;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 시연 재시작 규칙 (기획안 v4 3-4, NMJ 결정 — 1차안)
    ///
    /// - 길에서 쓰러지면: NGH Game Over 화면 → 아무 키 → 그 자리에서 일어난다 (NGH 기본 동작 그대로).
    /// - 보스 아레나 안에서 쓰러지면: 일어난 뒤 먹물 화면 전환으로 덮고 아레나 입구로 옮긴다.
    ///   (현무 체력·위치는 BHS 쪽 초기화 기능이 생기면 여기서 함께 되돌린다)
    ///
    /// NMJ_Main 씬에 둔다. Arena Area = 아레나를 덮는 콜라이더(트리거), Arena Spawn = 아레나 입구.
    /// </summary>
    public class RespawnRule : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [Tooltip("이 안에서 쓰러지면 아레나 입구로 돌아간다")]
        [SerializeField] private Collider arenaArea;
        [SerializeField] private Transform arenaSpawn;
        [SerializeField] private InkStyle transition = InkStyle.Bleed;
        [SerializeField] private bool logRespawn = true;

        private bool wasDead;
        private bool diedInArena;

        private void Update()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
                if (player == null) return;
            }

            bool dead = player.IsDead;
            if (dead && !wasDead) diedInArena = InArena(player.transform.position);
            if (!dead && wasDead) OnRespawned();
            wasDead = dead;
        }

        private bool InArena(Vector3 p) => arenaArea != null && arenaArea.bounds.Contains(p + Vector3.up * 0.5f);

        private void OnRespawned()
        {
            if (!diedInArena || arenaSpawn == null) return;
            InkTransition.Play(transition, () =>
            {
                Transform body = player.transform;
                var cc = body.GetComponent<CharacterController>();
                bool on = cc != null && cc.enabled;
                if (on) cc.enabled = false;
                body.SetPositionAndRotation(arenaSpawn.position, arenaSpawn.rotation);
                if (on) cc.enabled = true;
                Physics.SyncTransforms();
                if (logRespawn) Debug.Log("[NMJ] 아레나에서 쓰러져 아레나 입구로 돌아감", this);
            }, 0.8f);
        }
    }
}
