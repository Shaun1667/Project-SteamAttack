using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 충격파 화면 연출: 강한 타격 순간 그 자리에서 물결 고리가 퍼지며 화면이 출렁임.
    ///  - 플레이어가 강공격 피격(넘어짐)을 당했을 때: 플레이어 자리에서
    ///  - 플레이어 강공격이 적·보스에 맞았을 때: 맞은 대상 자리에서
    ///  - 화면 효과는 NGH_ScreenFX 셰이더가 전역 값 _NGH_Shock(진행도)·_NGH_ShockCenter(화면 위치)를 읽어 그림
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class ScreenShockwave : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;
        public Camera targetCamera;

        [Header("언제")]
        public bool onHeavyHitTaken = true;
        public bool onHeavyAttackLanded = true;

        [Header("시간 (초, 실제 시간 기준)")]
        [Tooltip("고리가 화면 끝까지 퍼지는 시간")]
        [Min(0.05f)] public float duration = 0.45f;

        static readonly int ShockId = Shader.PropertyToID("_NGH_Shock");
        static readonly int CenterId = Shader.PropertyToID("_NGH_ShockCenter");

        float _t = -1f;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            if (!player) return;
            player.Damaged += OnDamaged;
            player.OnHitLanded += OnHitLanded;
        }

        void OnDisable()
        {
            if (player)
            {
                player.Damaged -= OnDamaged;
                player.OnHitLanded -= OnHitLanded;
            }
            _t = -1f;
            Shader.SetGlobalFloat(ShockId, 0f);
        }

        void OnDamaged(int damage, bool heavy)
        {
            if (heavy && onHeavyHitTaken) Play(player.transform.position + Vector3.up * 1.0f);
        }

        void OnHitLanded(Damageable target)
        {
            if (!onHeavyAttackLanded || player.CurrentAction != PlayerController.ActionState.HeavyAttack || target == null) return;
            Collider c = target.GetComponentInChildren<Collider>();
            Play(c ? c.bounds.center : target.transform.position + Vector3.up);
        }

        /// <summary>월드 위치에서 충격파를 한 번 퍼뜨림</summary>
        public void Play(Vector3 worldPos)
        {
            if (!targetCamera)
            {
                var tpc = FindAnyObjectByType<ThirdPersonCamera>();
                targetCamera = tpc ? tpc.GetComponent<Camera>() : Camera.main;
            }
            Vector2 center = new Vector2(0.5f, 0.5f);
            if (targetCamera)
            {
                Vector3 vp = targetCamera.WorldToViewportPoint(worldPos);
                if (vp.z > 0f) center = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
            }
            Shader.SetGlobalVector(CenterId, center);
            _t = 0f;
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime / duration;
            if (_t >= 1f) { _t = -1f; Shader.SetGlobalFloat(ShockId, 0f); return; }
            Shader.SetGlobalFloat(ShockId, Mathf.Max(0.001f, 1f - (1f - _t) * (1f - _t)));   // 처음엔 빠르게, 끝에서 느리게 퍼짐
        }
    }
}
