namespace Domain.Entities;

// 註：不放 [Required]/[MaxLength] DataAnnotation——Dapper 不看它、此實體也不是 API 綁定型別，
// 貼了等於裝飾（舊 MaxLength(5) 還是錯的，職業名有 6 字）。輸入驗證放 CharacterRequest DTO；
// required 修飾詞保留（編譯期建構保護）。
public class Character
{
    public required string Id { get; set; }
    public ulong DiscordId { get; set; }
    public required string Name { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }

    /// <summary>
    /// 楓葉祝福等級（自填，0=無；leader-led，見計畫 §9.18）。Phase 1a：欄位已在 DB（migration 000009，
    /// DEFAULT 0），此屬性先落地；repo 讀寫映射待 1b/1c 有消費者時再接（維持不改行為）。
    /// </summary>
    public int MapleBlessingLevel { get; set; }
}
