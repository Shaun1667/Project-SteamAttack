using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 씬 이동 사이에 플레이어 상태를 넘겨주는 저장소 (NGH)
    /// - NGH_ScenePortal이 씬을 옮기기 직전에 Save()로 현재 체력을 담아 두고,
    ///   다음 씬의 NGH_PlayerController가 시작할 때 TakeHp()로 꺼내 그대로 이어 간다.
    /// - 포탈 없이 씬을 바로 실행하면 담긴 값이 없으므로 원래대로(최대 체력) 시작한다.
    /// </summary>
    public static class PlayerCarryOver
    {
        static bool _hasHp;
        static int _hp;

        /// <summary>씬 이동 직전에 호출 — 현재 체력을 담아 둔다</summary>
        public static void Save(PlayerController player)
        {
            if (player == null || player.IsDead) return;
            _hp = player.Hp;
            _hasHp = true;
        }

        /// <summary>담아 둔 체력을 꺼낸다 (한 번 꺼내면 비워짐)</summary>
        public static bool TakeHp(out int hp)
        {
            hp = _hp;
            if (!_hasHp) return false;
            _hasHp = false;
            return true;
        }

        // 플레이 모드를 다시 시작할 때 지난 값이 남지 않게 (Domain Reload를 끈 경우 대비)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { _hasHp = false; _hp = 0; }
    }
}
