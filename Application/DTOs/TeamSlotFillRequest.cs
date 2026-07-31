namespace Application.DTOs;

/// <summary>
/// 玩家補位：把自己的角色加進某個隊伍。型別上刻意不帶 DiscordId——一律用登入身分（currentDiscordId），
/// 不信任 client 傳的值，補位天生不可能填成別人。
/// </summary>
public class TeamSlotFillRequest
{
    public int TeamSlotId { get; set; }
    public string? DiscordName { get; set; }
    public required string CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
}
