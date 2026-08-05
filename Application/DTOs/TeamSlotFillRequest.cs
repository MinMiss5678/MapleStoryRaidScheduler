using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

/// <summary>
/// 玩家補位：把自己的角色加進某個隊伍。型別上刻意不帶 DiscordId——一律用登入身分（currentDiscordId），
/// 不信任 client 傳的值，補位天生不可能填成別人。
/// </summary>
public class TeamSlotFillRequest
{
    public int TeamSlotId { get; set; } // 查無由 FillSlotAsync 擋成 400

    public string? DiscordName { get; set; }

    [Required]
    public required string CharacterId { get; set; }

    public string? CharacterName { get; set; }

    [Required]
    public required string Job { get; set; }

    public int AttackPower { get; set; }

    [Range(0, int.MaxValue)]
    public int Rounds { get; set; }
}
