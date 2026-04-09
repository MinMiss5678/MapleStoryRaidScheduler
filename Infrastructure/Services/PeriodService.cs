using Application.DTOs;
using Application.Interface;
using Application.Queries;

namespace Infrastructure.Services;

public class PeriodService : IPeriodService
{
    private readonly IPeriodQuery _periodQuery;

    public PeriodService(IPeriodQuery periodQuery)
    {
        _periodQuery = periodQuery;
    }
    
    public async Task<PeriodDto?> GetByNowAsync()
    {
        var period = await _periodQuery.GetByNowAsync();
        if (period == null) return null;
        var periodDtos = new PeriodDto()
        {
            Id = period.Id,
            StartDate = period.StartDate.ToOffset(TimeSpan.FromHours(8)).DateTime,
            EndDate = period.EndDate.ToOffset(TimeSpan.FromHours(8)).DateTime,
        };

        return periodDtos;
    }
}