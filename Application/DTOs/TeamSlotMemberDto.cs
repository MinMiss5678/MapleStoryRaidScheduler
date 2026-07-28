namespace Application.DTOs;

/// <summary>
/// TeamSlotCharacter 的 DTO，同時用於 API 請求（補位/更新）與回應（隊伍成員清單）。
/// </summary>
public class TeamSlotMemberDto
{
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; } = string.Empty;
    public string? CharacterId { get; set; } // null 表示空位
    public string? CharacterName { get; set; }
    public string Job { get; set; } = string.Empty; // 具體職業或 JobCategory 需求
    public int AttackPower { get; set; }
    public int Level { get; set; }
    public int Rounds { get; set; }
    public bool IsManual { get; set; } // 手動補位/管理員微調，排團邏輯不覆蓋

    /// <summary>樂觀鎖版本（讀取時帶出，存檔時原樣送回；新成員無此值）。</summary>
    public string? Version { get; set; }
}
