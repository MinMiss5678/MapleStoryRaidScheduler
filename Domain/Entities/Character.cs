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
}
