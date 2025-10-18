using Application.DTOs;
using Domain.Entities;

namespace Application.Queries;

public interface IPlayerRegisterQuery
{
    Task<IEnumerable<PlayerRegisterSchedule>> GetByNowPeriodIdAsync(int bossId);
    Task<IEnumerable<PlayerRegisterSchedule>> GetByQueryAsync(RegisterGetByQueryRequest request, int periodId);
}