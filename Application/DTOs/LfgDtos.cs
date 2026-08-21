namespace Application.DTOs;

/// <summary>發布即時找隊意圖（DiscordId 由 Controller 注入）。BossId 必填。</summary>
public class LfgIntentCreateCommand
{
    public ulong DiscordId { get; set; }
    public required string CharacterId { get; set; }
    public int BossId { get; set; }
}

/// <summary>即時看板一筆（period-less §8 Phase 3）。</summary>
public class LfgBoardItemDto
{
    public int Id { get; set; }
    public string CharacterId { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string Job { get; set; } = "";
    public int AttackPower { get; set; }
    public int BossId { get; set; }
    public string BossName { get; set; } = "";
}
