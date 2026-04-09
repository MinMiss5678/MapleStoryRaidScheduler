using Domain.Entities;

namespace Application.Interface;

public interface IScheduleService
{
    Task<IEnumerable<TeamSlot>> AutoScheduleWithTemplateAsync(int bossId, int templateId);

    DateTimeOffset GetDateTimeFromPeriod(DateTimeOffset periodStart, DateTimeOffset periodEnd, int weekday,
        TimeOnly startTime, string timeZoneId);
}