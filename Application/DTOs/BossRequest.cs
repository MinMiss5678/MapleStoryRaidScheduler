namespace Application.DTOs;

public class BossRequest
{
    public string Name { get; set; } = string.Empty;
    public int RequireMembers { get; set; }
    public int RoundConsumption { get; set; } = 1;
}
