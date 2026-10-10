using UnityEngine;

namespace NGH
{
    /// <summary>무기 하나의 데이터. 무기 모델(손에 붙은 오브젝트)에 붙입니다.</summary>
    public class Weapon : MonoBehaviour
    {
        public string weaponName = "환도";
        [Tooltip("근접 무기만 락온 가능")]
        public bool isMelee = true;
        public float lightDamage = 1f;   // 좌클릭: 모션당 1
        public float heavyDamage = 3f;   // 강공격
        [Tooltip("공격 판정 거리(캐릭터 앞쪽)")]
        public float reach = 1.17f;
        [Tooltip("공격 판정 반경")]
        public float hitRadius = 0.78f;
    }
}
