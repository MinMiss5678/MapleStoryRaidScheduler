namespace Domain.Entities;

public class PlayerCharacterRegister
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int[] Weekdays { get; set; }
    public string[] Timeslots { get; set; }
    public int? CharacterRegisterId { get; set; }
    public string? CharacterId { get; set; }
    public string? Job { get; set; }
    public int? BossId { get; set; }
    public int? Rounds { get; set; }
}