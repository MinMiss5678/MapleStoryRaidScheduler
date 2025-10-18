using Domain.Entities;

namespace Application.Interface;

public interface IScheduleService
{
    Task<IEnumerable<TeamSlot>> AutoScheduleAsync(int bossId, int minMembers);

    Task<DateTimeOffset> GetDateTimeFromPeriod(DateTimeOffset periodStart, DateTimeOffset periodEnd, int weekday,
        string timeslot, string timeZoneId);
}