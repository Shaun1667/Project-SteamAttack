using UnityEngine;

namespace NGH
{
    /// <summary>손에 든 칼이 보이면 칼집 속 칼을 숨기고, 납도하면 다시 보이게 함.</summary>
    public sealed class SheathSwordVisual : MonoBehaviour
    {
        [Tooltip("손에 든 칼 (WeaponHolder가 발도·납도에 맞춰 켜고 끔)")]
        public GameObject drawnSword;
        [Tooltip("칼집에 꽂힌 칼 모델")]
        public Renderer sheathedSword;

        void LateUpdate()
        {
            if (drawnSword && sheathedSword) sheathedSword.enabled = !drawnSword.activeInHierarchy;
        }
    }
}
