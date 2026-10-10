using UnityEngine;

namespace BHS
{
    public class BossWallHealth : MonoBehaviour
    {
        [Header("벽 체력")]
        public float maxHp = 20f;

        public float currentHp;
        private bool isBroken;
        private BossHealth bossHealth;

        private void Awake()
        {
            currentHp = maxHp;

            // 부모에 있는 보스 체력 스크립트를 찾음
            bossHealth = GetComponentInParent<BossHealth>();

            if (bossHealth == null)
                Debug.LogError($"{name}: 부모에 BossHealth가 없습니다.", this);
        }

        public void TakeDamage(float amount, Vector3 from)
        {
            if (isBroken || amount <= 0f)
                return;

            if (bossHealth == null || !bossHealth.IsAlive)
                return;

            currentHp = Mathf.Max(0f, currentHp - amount);

            Debug.Log($"{name} 피격: {currentHp}/{maxHp}");

            if (currentHp > 0f)
                return;

            // 같은 벽의 파괴가 여러 번 집계되지 않도록 처리
            isBroken = true;

            gameObject.SetActive(false);
            bossHealth.OnWallDestroyed();
        }
    }
}