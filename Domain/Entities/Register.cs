namespace Domain.Entities;

public class Register
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public int PeriodId { get; set; }
    public int[] Weekdays { get; set; }
    public string[] Timeslots { get; set; }
    public List<CharacterRegister> CharacterRegisters { get; set; }
    public List<int> DeleteCharacterRegisterIds { get; set; } = [];
}