using System;
using System.Collections.Generic;
using UnityEngine;

namespace YPH
{
    /// <summary>켜진 받아치기 대상과 공격 예고를 공유합니다. 목록을 순회하는 중에는 대상을 등록·해제하지 않습니다.</summary>
    public static class ParryTargets
    {
        private static readonly List<IParryTarget> _active = new List<IParryTarget>();

        /// <summary>현재 등록된 대상의 읽기 전용 목록입니다. 플레이어가 거리·타이밍으로 대상을 고릅니다.</summary>
        public static IReadOnlyList<IParryTarget> Active => _active;
        /// <summary>받아칠 수 있는 공격 시작 때 대상과 두 원이 겹칠 때까지 남은 초를 보냅니다.</summary>
        public static event Action<IParryTarget, float> AttackStarted;

        /// <summary>도메인 리로드를 꺼도 이전 Play의 대상과 구독자가 남지 않도록 시작할 때 비웁니다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active.Clear();
            AttackStarted = null;
        }

        /// <summary>대상의 OnEnable에서 등록합니다. null과 중복 등록은 무시합니다.</summary>
        public static void Register(IParryTarget target)
        {
            if (target == null || _active.Contains(target))
            {
                return;
            }
            _active.Add(target);
        }

        /// <summary>대상의 OnDisable에서 해제합니다. 이미 해제된 대상도 안전하게 무시합니다.</summary>
        public static void Unregister(IParryTarget target)
        {
            _active.Remove(target);
        }

        /// <summary>대상이 창과 겹침 시각을 설정한 뒤 한 번 호출해 동심원 예고를 시작합니다.</summary>
        public static void NotifyAttackStarted(IParryTarget target, float secondsUntilOverlap)
        {
            if (target == null)
            {
                return;
            }
            AttackStarted?.Invoke(target, secondsUntilOverlap);
        }
    }
}
