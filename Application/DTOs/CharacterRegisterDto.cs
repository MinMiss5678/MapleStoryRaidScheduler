namespace Application.DTOs;

public class CharacterRegisterDto
{
    public int? Id { get; set; }
    public string? CharacterId { get; set; }
    public string? Job { get; set; }
    public int? BossId { get; set; }
    public int? Rounds { get; set; }
}