using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 열 아지랑이: 증기통에서 증기가 빠질 때와 총을 쏠 때 그 자리 공기가 일렁이게 함.
    ///  - 아지랑이 파티클(NGH_HeatHaze 셰이더)을 증기 배출구·총구 위치에서 뿜음
    ///  - YPH 증기통(SteamTank.OnConsumed)·총(SteamGun.OnFired) 알림만 받아 쓰고 그쪽 파일은 고치지 않음
    /// 플레이어에 붙이고, 아지랑이 파티클 시스템 두 개를 연결합니다.
    /// </summary>
    public class HeatHazeEmitter : MonoBehaviour
    {
        [Header("참조 (비우면 자식에서 자동으로 찾음)")]
        public YPH.SteamTank tank;
        public YPH.SteamGun gun;
        [Tooltip("증기 배출구 위치에 둔 아지랑이 파티클 (증기통 배출구 파티클의 자식으로 둠)")]
        public ParticleSystem ventHaze;
        [Tooltip("총구 아지랑이 파티클 (월드 공간에서 뿜음)")]
        public ParticleSystem muzzleHaze;

        [Header("양")]
        [Tooltip("증기 1당 아지랑이 알갱이 수")]
        [Min(0f)] public float perPressure = 0.15f;
        [Min(1)] public int maxVentBurst = 12;
        [Min(1)] public int muzzleBurst = 6;

        void Awake()
        {
            if (!tank) tank = GetComponentInChildren<YPH.SteamTank>(true);
            if (!gun) gun = GetComponentInChildren<YPH.SteamGun>(true);
        }

        void OnEnable()
        {
            if (tank) tank.OnConsumed += OnConsumed;
            if (gun) gun.OnFired += OnFired;
        }

        void OnDisable()
        {
            if (tank) tank.OnConsumed -= OnConsumed;
            if (gun) gun.OnFired -= OnFired;
        }

        void OnConsumed(float amount)
        {
            if (!ventHaze) return;
            if (!ventHaze.isPlaying) ventHaze.Play(false);   // YPH 배출구가 자식까지 멈출 때가 있어 다시 켬
            ventHaze.Emit(Mathf.Clamp(Mathf.CeilToInt(amount * perPressure), 1, maxVentBurst));
        }

        void OnFired(Vector3 muzzle, Vector3 end, bool hitEnemy)
        {
            if (!muzzleHaze) return;
            if (!muzzleHaze.isPlaying) muzzleHaze.Play(false);
            var p = new ParticleSystem.EmitParams { position = muzzle, applyShapeToPosition = true };
            muzzleHaze.Emit(p, muzzleBurst);
        }
    }
}
