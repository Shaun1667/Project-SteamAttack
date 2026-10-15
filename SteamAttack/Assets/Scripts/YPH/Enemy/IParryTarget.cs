using UnityEngine;

namespace YPH
{
    /// <summary>적과 보스가 공통으로 제공하는 받아치기 정보입니다. 입력·타이밍 판정은 플레이어 쪽에서 처리합니다.</summary>
    public interface IParryTarget
    {
        /// <summary>동심원을 그리고 거리를 재는 월드 위치입니다. 예를 들어 창끝이나 발톱 끝을 반환합니다.</summary>
        Vector3 ParryPoint { get; }
        /// <summary>지금 받아칠 수 있는 공격의 창이 열려 있는지 반환합니다.</summary>
        bool IsParryWindowOpen { get; }
        /// <summary>두 원이 겹치는 시각입니다. Time.time 기준이며 플레이어가 이 시각과 입력 시각을 비교합니다.</summary>
        float ParryOverlapTime { get; }
        /// <summary>받아치기로 자세가 무너진 상태인지 반환합니다. 동심원 성공 연출을 유지할 때 사용합니다.</summary>
        bool IsParryStunned { get; }
        /// <summary>플레이어의 판정 성공 후 공격 취소·자세 붕괴·약점 노출을 적용합니다. 창이 닫혔으면 false입니다.</summary>
        bool ApplyParry(float damage, Vector3 from);
    }
}
