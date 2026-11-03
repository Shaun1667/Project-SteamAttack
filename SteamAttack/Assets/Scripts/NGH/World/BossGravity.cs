using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 현무(BHS_HyunMu) 중력.
    /// 보스는 CharacterController로 수평으로만 움직여서(BossFSM·BossDashAttack은 높이를 0으로 둠),
    /// 턱·돌·플레이어 위로 올라타면 다시 내려오지 않고 떠 있었다 → 공중에 있으면 아래로 떨어뜨린다.
    /// BHS 보스 코드는 고치지 않고 같은 오브젝트에 붙인다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(200)]   // 보스 이동(BossFSM·돌진)이 끝난 뒤
    public class BossGravity : MonoBehaviour
    {
        [Tooltip("중력 가속도 (m/s², 음수)")]
        [SerializeField] private float gravity = -25f;
        [Tooltip("최대 낙하 속도 (m/s)")]
        [SerializeField, Min(1f)] private float maxFallSpeed = 30f;
        [Tooltip("땅에 붙어 있게 누르는 속도 (m/s). 경사·턱을 내려갈 때 뜨지 않게")]
        [SerializeField, Min(0f)] private float groundStick = 2f;

        private CharacterController controller;
        private float verticalSpeed;

        private void Awake() => controller = GetComponent<CharacterController>();

        private void Update()
        {
            if (controller == null || !controller.enabled || Time.deltaTime <= 0f) return;
            if (controller.isGrounded) verticalSpeed = -groundStick;
            else verticalSpeed = Mathf.Max(verticalSpeed + gravity * Time.deltaTime, -maxFallSpeed);
            controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
        }
    }
}
