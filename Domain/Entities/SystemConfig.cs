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
    /// <param name="periodStartDate">週期開始日期 (通常為週四 00:00)</param>
    /// <returns>該週期的報名截止日期時間</returns>
    public DateTimeOffset GetDeadlineForPeriod(DateTimeOffset periodStartDate)
    {
        // 截止日永遠在週期開始之前（上一週）
        // 例如：週期開始是週四 04/16，截止是上週日 04/12
        // (0 - 4 + 7) % 7 = 3，再 -7 = -4 天 → 04/16 - 4 = 04/12
        int daysToAdd = ((int)DeadlineDayOfWeek - (int)periodStartDate.DayOfWeek + 7) % 7 - 7;

        return periodStartDate.Date.AddDays(daysToAdd).Add(DeadlineTime);
    }
}
