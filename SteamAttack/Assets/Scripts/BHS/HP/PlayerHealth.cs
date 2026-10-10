using UnityEngine;

namespace BHS
{
    public class PlayerHealth : MonoBehaviour
    {
        public float hp = 5f;

        public void TakeDamage(float damage)
        {
            if (hp <= 0f) return;

            hp = Mathf.Max(0f, hp - damage);
            Debug.Log($"플레이어 체력: {hp}", this);

            if (hp <= 0f)
                Debug.Log("플레이어 사망", this);
        }
    }
}