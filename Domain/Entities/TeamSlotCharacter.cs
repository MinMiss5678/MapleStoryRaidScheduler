namespace Domain.Entities;

public class TeamSlotCharacter
{
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public ulong DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public string? CharacterId { get; set; } // 如果為 null 表示是空位
    public string? CharacterName { get; set; }
    public required string Job { get; set; } // 可能是具體職業或是 JobCategory 需求
    public int AttackPower { get; set; }
    public int Level { get; set; }
    public int Rounds { get; set; }
    public bool IsManual { get; set; } // 是否為玩家手動補位或管理員手動微調，排團邏輯不應覆蓋

    /// <summary>
    /// 入隊狀態（leader-led，見 <see cref="TeamSlotMemberStatus"/>）。只有 Confirmed 占容量。
    /// Phase 1a：欄位已在 DB（migration 000009，DEFAULT 'Confirmed'），此屬性先實作；
    /// repo 讀寫映射待 1b/1c 有消費者時再接（現無人讀寫，故不動既有查詢/INSERT，維持不改行為）。
    /// </summary>
    public string Status { get; set; } = TeamSlotMemberStatus.Confirmed;

    /// <summary>
    /// 打王時刻的去正規化副本（leader-led，migration 000011）：用於跨隊時段重疊的 DB unique
    /// （Postgres unique 不能跨表，故複製一份；snapshot 語意）。邀請/開隊時由隊時間填；舊 auto-assign 路徑不填（null）。
    /// </summary>
    public DateTimeOffset? SlotDateTime { get; set; }

    /// <summary>樂觀鎖版本（Postgres xmin，轉字串）。更新時比對，對不上代表這期間被別的流程動過。</summary>
    public string? Version { get; set; }
}
