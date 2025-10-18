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
    
    public async Task<PeriodDto> GetByNowAsync()
    {
        var period = await _periodQuery.GetByNowAsync();
        var periodDtos = new PeriodDto()
        {
            Id = period.Id,
            StartDate = period.StartDate.Date,
            EndDate = period.EndDate.Date,
        };

        return periodDtos;
    }
}