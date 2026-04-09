using Domain.Entities;

namespace Application.DTOs;

public class RegisterUpdateCommand
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public int PeriodId { get; set; }
    public List<CharacterRegister> CharacterRegisters { get; set; } = [];
    public List<PlayerAvailability> Availabilities { get; set; } = [];
    public List<int> DeleteCharacterRegisterIds { get; set; } = [];
}
