using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

// 這是 API 綁定的 request 型別，[ApiController] 會自動驗證這些 annotation → 不合就回 400。
// （放在實體上的 annotation 對 Dapper 無效、實體也不是綁定型別，故驗證放這裡才有作用。）
public class CharacterRequest
{
    [Required]
    [MaxLength(5)] // 角色代碼＝遊戲內 ID，長度上限 5（前端 CharacterForm 角色代碼欄亦 maxLength=5）
    public string Id { get; set; } = string.Empty;

    public ulong DiscordId { get; set; } // 由 Controller 從 Claims 注入，不驗證

    [Required]
    [MaxLength(20)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)] // 職業名最長 6 字（火毒大魔導士），給寬鬆上限；不用 5（實體舊值是錯的）
    public string Job { get; set; } = string.Empty;

    [Range(0, int.MaxValue)] // 攻擊力非負
    public int AttackPower { get; set; }

    [Range(1, 200)] // 人物等級 1–200（自填，遊戲現行等級上限 200）
    public int Level { get; set; }
}
