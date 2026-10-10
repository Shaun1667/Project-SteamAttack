using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BHS
{
    public class DummyPlayerShooter : MonoBehaviour
    {
        [Header("발사 설정")]
        public Transform firePoint;
        public DummyBullet bulletPrefab;

        public float damage = 2f;

        [Min(0.01f)]
        public float fireInterval = 0.2f;


        [Header("미리 생성할 탄환 개수")]
        [Min(1)]
        public int poolSize = 20;

        private DummyBullet[] bulletPool;
        private float nextFireTime;

        private void Awake()
        {
            if (firePoint == null || bulletPrefab == null)
            {
                Debug.LogError("Fire Point와 Bullet Prefab을 연결해주세요.", this);
                enabled = false;
                return;
            }

            bulletPool = new DummyBullet[poolSize];

            for (int i = 0; i < poolSize; i++)
            {
                // 플레이어가 움직여도 탄환이 따라가지 않도록
                // 플레이어의 자식으로 만들지 않음
                DummyBullet bullet = Instantiate(bulletPrefab);

                bullet.gameObject.SetActive(false);
                bulletPool[i] = bullet;
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            bool isFiring =
                Mouse.current != null &&
                Mouse.current.leftButton.isPressed;
#else
            bool isFiring = Input.GetMouseButton(0);
#endif

            if (!isFiring || Time.time < nextFireTime)
                return;

            nextFireTime = Time.time + fireInterval;
            Fire();
        }

        private void Fire()
        {
            foreach (DummyBullet bullet in bulletPool)
            {
                if (bullet.gameObject.activeSelf)
                    continue;

                bullet.Launch(
                    firePoint.position,
                    firePoint.forward,
                    damage
                );

                return;
            }

            // 모든 탄환을 사용 중이면 이번 발사는 건너뜀
        }

        private void OnDestroy()
        {
            // 플레이어가 제거될 때 풀도 정리
            if (bulletPool == null)
                return;

            foreach (DummyBullet bullet in bulletPool)
            {
                if (bullet != null)
                    Destroy(bullet.gameObject);
            }
        }
    }
}