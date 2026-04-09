namespace Domain.Entities;

public class TeamSlotCharacter
{
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; }
    public string? CharacterId { get; set; } // 如果為 null 表示是空位
    public string? CharacterName { get; set; }
    public string Job { get; set; } // 可能是具體職業或是 JobCategory 需求
    public int AttackPower { get; set; }
    public int Level { get; set; }
    public int Rounds { get; set; }
    public bool IsManual { get; set; } // 是否為玩家手動補位或管理員手動微調，排團邏輯不應覆蓋
}