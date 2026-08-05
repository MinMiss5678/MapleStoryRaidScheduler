namespace Application.DTOs;

public class RegisterCreateCommand
{
    public ulong DiscordId { get; set; } // 由 Controller 從 Claims 注入，不驗證

    public int PeriodId { get; set; } // 存在性由 RegisterService.CreateAsync 驗（不存在回 404）

    public List<CharacterRegisterDto> CharacterRegisters { get; set; } = [];
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
}
