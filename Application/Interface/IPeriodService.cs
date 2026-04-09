using Application.DTOs;

namespace Application.Interface;

public interface IPeriodService
{
    Task<PeriodDto?> GetByNowAsync();
}