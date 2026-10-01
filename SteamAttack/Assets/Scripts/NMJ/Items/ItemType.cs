namespace SteamAttack.Items
{
    /// <summary>아이템 분류. 인벤토리 정렬/필터와 퀘스트 판정에 쓰인다.</summary>
    public enum ItemType
    {
        Etc = 0,
        Equipment = 1,
        Consumable = 2,
        Material = 3,
        Quest = 4,
    }

    /// <summary>아이템 등급. UI 색상 표시용.</summary>
    public enum ItemGrade
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Unique = 3,
        Legendary = 4,
    }
}
