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
    /// 報名截止時間 (保留用於舊程式碼相容)
    /// </summary>
    public DateTimeOffset RegistrationDeadline { get => DateTimeOffset.MaxValue; set { } }

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
        // 計算週期開始 (週四) 到目標星期幾的差距
        // 例如：週期開始是週四 (4)，目標是週三 (3)
        // (3 - 4 + 7) % 7 = 6 天後
        int daysToAdd = ((int)DeadlineDayOfWeek - (int)periodStartDate.DayOfWeek + 7) % 7;
        
        // 如果截止日就是開始日且截止時間已過，通常代表截止日是下一週的同一天
        // 但在我們的系統中，週期是一週一次 (週四到下週三)，所以 daysToAdd 0~6 是合理的
        
        return periodStartDate.Date.AddDays(daysToAdd).Add(DeadlineTime);
    }
}
