using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class RegisterUpdateCommand
{
    // 刻意不驗 Id：後端不信任 client 傳的 Id，改由 (discordId, periodId) 查出自己的 registerId
    // （IDOR 防護，見 RegisterService.UpdateAsync）。加 [Range] 反而會誤擋合法更新。
    public int Id { get; set; }
    public ulong DiscordId { get; set; } // 由 Controller 從 Claims 注入，不驗證

    [Range(1, int.MaxValue)]
    public int PeriodId { get; set; }

    public List<CharacterRegisterDto> CharacterRegisters { get; set; } = [];
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
    public List<int> DeleteCharacterRegisterIds { get; set; } = [];
}
