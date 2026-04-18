namespace Application.DTOs;

public class CharacterRequest
{
    public string Id { get; set; } = string.Empty;
    public ulong DiscordId { get; set; } // 由 Controller 從 Claims 注入
    public string Name { get; set; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public int AttackPower { get; set; }
}
