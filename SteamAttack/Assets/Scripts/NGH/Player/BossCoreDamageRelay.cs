using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 병합용 연결 (NGH) — 현무 코어 HP(BHS BossCoreHealth)를 플레이어 공격과 잇는다. BossCoreHealth 와 같은 오브젝트에 붙인다.
    /// - YPH 총·수류탄: YPH.IDamageable 로 피해를 받아 BossCoreHealth.TakeDamage 로 넘긴다. (피격 콜라이더의 부모에서 찾힘)
    /// - NGH 근접: 근접 판정은 NGH.Damageable 만 찾으므로, 코어의 피격 콜라이더(Boss 태그) 오브젝트마다 수신용 Damageable 을 실행 중에 붙이고
    ///   깎인 만큼을 코어로 옮긴 뒤 수신용 HP 는 다시 채운다. 다리 뼈에 직접 붙이면 Damageable 의 밀림이 다리를 움직이므로 콜라이더 쪽에 붙인다.
    /// - 코어가 피해를 받을 수 없을 때(전투 시작 전 메인코어, 파괴 후)는 수신용 Damageable 을 꺼서 락온에서도 빠지게 한다.
    /// 증기 회복(YPH.CombatTags)이 보스로 알아보도록 태그 비교는 Boss 로 답한다.
    /// - 개발자 창 원펀맨이 켜져 있으면 맞은 다리·신핵이 한 대에 부서진다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossCoreHealth))]
    public class BossCoreDamageRelay : MonoBehaviour, YPH.IDamageable
    {
        [Tooltip("수신용 NGH.Damageable 의 HP. 한 번에 다 깎이지 않도록 크게 둔다")]
        [SerializeField, Min(10f)] private float receiverHp = 1000f;
        [Tooltip("락온 표시 높이(피격 콜라이더 기준)")]
        [SerializeField] private float markerHeight = 0.4f;

        private BossCoreHealth core;
        private readonly List<Damageable> receivers = new List<Damageable>();
        private Transform player;

        public bool IsAlive => core != null && core.CanTakeDamage;

        public void TakeDamage(float amount, Vector3 hitFrom)
        {
            if (core != null) core.TakeDamage(OnePunch(amount), hitFrom);
        }

        // 개발자 창 원펀맨: 다리·신핵도 한 대에 부서지게 남은 HP 전부를 준다
        private float OnePunch(float amount) => NMJ.NmjDevWindow.OnePunch && core != null ? Mathf.Max(amount, core.CurrentHp) : amount;

        bool YPH.IDamageable.CompareTag(string tag) => tag == YPH.CombatTags.BossTag || CompareTag(tag);

        private void Awake()
        {
            core = GetComponent<BossCoreHealth>();
        }

        // BossCoreHealth.Awake 가 자동 캡슐을 만든 뒤에 찾는다
        private void Start()
        {
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
            {
                if (!col.CompareTag(YPH.CombatTags.BossTag) || col.GetComponentInParent<BossCoreHealth>() != core) continue;
                Damageable receiver = col.GetComponent<Damageable>();
                if (receiver == null) receiver = col.gameObject.AddComponent<Damageable>();
                receiver.markerHeight = markerHeight;
                receiver.maxHp = receiverHp;
                receiver.hp = receiverHp;
                receivers.Add(receiver);
            }
            if (receivers.Count == 0) Debug.LogWarning($"[NGH] {name}: Boss 태그 피격 콜라이더가 없어 근접 공격을 받을 수 없습니다.", this);
        }

        private void LateUpdate()
        {
            bool open = IsAlive;
            foreach (Damageable receiver in receivers)
            {
                if (receiver == null) continue;
                if (receiver.enabled != open)
                {
                    if (!open) receiver.SetLocked(false);
                    receiver.enabled = open;
                }
                if (receiver.hp >= receiverHp) continue;

                float lost = receiverHp - receiver.hp;
                receiver.hp = receiverHp;
                if (!open) continue;
                if (player == null)
                {
                    GameObject found = GameObject.FindGameObjectWithTag(YPH.CombatTags.PlayerTag);
                    player = found != null ? found.transform : null;
                }
                core.TakeDamage(OnePunch(lost), player != null ? player.position : transform.position);
            }
        }
    }
}
