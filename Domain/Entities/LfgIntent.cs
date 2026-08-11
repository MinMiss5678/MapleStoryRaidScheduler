namespace Domain.Entities;

/// <summary>
/// 即時找隊意圖（period-less §8 Phase 3）：玩家表達「接下來要打某王」，帶 TTL、與 period 無關。
/// 供即時看板顯示 + 即時團(Kind=Instant)的候選來源。到期即失效（讀取過濾 ExpiresAt > now）。
/// </summary>
public class LfgIntent
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public required string CharacterId { get; set; }
    public int? BossId { get; set; }   // null = 任意王
    public DateTimeOffset ExpiresAt { get; set; }
}
