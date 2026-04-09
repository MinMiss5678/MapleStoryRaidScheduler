namespace Domain.Entities;

public class Register
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public int PeriodId { get; set; }
    public List<CharacterRegister> CharacterRegisters { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
}