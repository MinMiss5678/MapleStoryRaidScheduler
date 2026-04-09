namespace Domain.Entities;

public class PlayerCharacterRegister
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
    public int? CharacterRegisterId { get; set; }
    public string? CharacterId { get; set; }
    public string? Job { get; set; }
    public int? BossId { get; set; }
    public int? Rounds { get; set; }
}