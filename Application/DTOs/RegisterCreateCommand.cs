using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class RegisterCreateCommand
{
    public ulong DiscordId { get; set; } // 由 Controller 從 Claims 注入，不驗證

    [Range(1, int.MaxValue)]
    public int PeriodId { get; set; }

    public List<CharacterRegisterDto> CharacterRegisters { get; set; } = [];
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
}
