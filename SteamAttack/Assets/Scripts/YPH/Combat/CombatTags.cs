
namespace YPH
{
    /// <summary>공격 대상의 편을 판단합니다. 태그 이름을 바꿀 때 이 파일에서만 변경합니다.</summary>
    public static class CombatTags
    {
        /// <summary>플레이어 피해 대상의 태그입니다.</summary>
        public const string PlayerTag = "Player";
        /// <summary>일반 적의 태그입니다.</summary>
        public const string EnemyTag = "Enemy";
        /// <summary>일반 적과 동일하게 증기를 회복시키는 보스 태그입니다.</summary>
        public const string BossTag = "Boss";

        /// <summary>살아 있는지와 무관하게 적의 편인지 판단합니다. 치명타 직후에도 회복할 수 있습니다.</summary>
        public static bool IsEnemy(IDamageable target)
        {
            return target != null && (target.CompareTag(EnemyTag) || target.CompareTag(BossTag));
        }

        /// <summary>플레이어인지 판단합니다. 플레이어 피격은 증기를 회복시키지 않습니다.</summary>
        public static bool IsPlayer(IDamageable target)
        {
            return target != null && target.CompareTag(PlayerTag);
        }
    }
}
