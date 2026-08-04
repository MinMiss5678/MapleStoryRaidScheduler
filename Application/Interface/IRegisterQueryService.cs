using Application.DTOs;

namespace Application.Interface;

public interface IRegisterQueryService
{
    Task<RegisterDto> GetAsync(ulong discordId);
    Task<RegisterDto> GetLastAsync(ulong discordId);
    Task<IEnumerable<TeamSlotMemberDto>> GetByQueryAsync(RegisterGetByQueryRequest request);

    /// <summary>
    /// 目前開放報名之週期的截止時間（與後端 EnsureRegistrationOpen 同一套：GetActivePeriodAsync
    /// → GetDeadlineForPeriod）。沒有 active period 時回 null。前端 banner 直接顯示這個值，
    /// 不自行用日曆週重算（避免前後端算法分歧、誤報已截止）。
    /// </summary>
    Task<DateTimeOffset?> GetCurrentDeadlineAsync();
}
