using UnityEngine;

namespace BHS
{
    public class DummyBullet : MonoBehaviour
    {
        [Header("탄환 설정")]
        [Min(0.01f)]
        public float moveSpeed = 20f;

        [Min(0.01f)]
        public float lifeTime = 3f;

        [Min(0.001f)]
        public float hitRadius = 0.05f;

        private Vector3 moveDirection;
        private Vector3 shotOrigin;

        private float damage;
        private float remainingTime;

        public void Launch(
            Vector3 position,
            Vector3 direction,
            float attackDamage)
        {
            moveDirection = direction.normalized;
            shotOrigin = position;

            damage = attackDamage;
            remainingTime = lifeTime;

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(moveDirection)
            );

            gameObject.SetActive(true);
        }

        private void Update()
        {
            remainingTime -= Time.deltaTime;

            if (remainingTime <= 0f)
            {
                ReturnToPool();
                return;
            }

            float moveDistance = moveSpeed * Time.deltaTime;

            if (Physics.SphereCast(
                transform.position,
                hitRadius,
                moveDirection,
                out RaycastHit hit,
                moveDistance,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide))
            {
                ApplyDamage(hit.collider);
                ReturnToPool();
                return;
            }

            transform.position += moveDirection * moveDistance;
        }

        private void ApplyDamage(Collider target)
        {
            BossWallHealth wall =
                target.GetComponentInParent<BossWallHealth>();

            if (wall != null)
            {
                // BossWallHealth가 붙은 오브젝트의 태그 확인
                if (wall.CompareTag("BossWall"))
                {
                    wall.TakeDamage(damage, shotOrigin);
                }

                return;
            }

            // 코어는 기존에 등록한 Collider로 확인
            BossHealth boss =
                target.GetComponentInParent<BossHealth>();

            if (boss != null && target == boss.coreCollider)
            {
                boss.TakeDamage(damage, shotOrigin);
            }
        }

        private void ReturnToPool()
        {
            gameObject.SetActive(false);
        }
    }
}