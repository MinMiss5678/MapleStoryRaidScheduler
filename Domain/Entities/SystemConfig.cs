using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class SystemConfig
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 截止報名的星期幾 (0 = Sunday, 1 = Monday, ..., 4 = Thursday, ...)
    /// </summary>
    public DayOfWeek DeadlineDayOfWeek { get; set; }

    /// <summary>
    /// 截止報名的當天時間 (例如 23:59:59)
    /// </summary>
    public TimeSpan DeadlineTime { get; set; }

    /// <summary>
    /// 是否已發送截止通知
    /// </summary>
    public bool IsDeadlineNotified { get; set; }

    /// <summary>
    /// 根據指定週期的開始日期，計算該週期的報名截止時間
    /// </summary>
    /// <param name="periodStartDate">週期開始日期（＝重製日 00:00，見 SlotDateCalculator.ResetDay）</param>
    /// <returns>該週期的報名截止日期時間</returns>
    public DateTimeOffset GetDeadlineForPeriod(DateTimeOffset periodStartDate)
    {
        // 截止日永遠在週期開始之前（上一週）；相對週期實際起始日推算，不綁死特定星期
        // 例如：週期開始週二 04/14、截止設週一 → (1 - 2 + 7) % 7 - 7 = -1 天 → 04/13
        int daysToAdd = ((int)DeadlineDayOfWeek - (int)periodStartDate.DayOfWeek + 7) % 7 - 7;

        return periodStartDate.Date.AddDays(daysToAdd).Add(DeadlineTime);
    }
}
