namespace Domain.Entities;

// SystemConfig 同時是 admin POST 綁定型別；但 [Key] 是 EF 映射提示、對 Dapper 無作用，故移除。
// DeadlineDayOfWeek 是 DayOfWeek enum（型別本身已約束 0-6）+ GetDeadlineForPeriod 取模能吸收，
// 不另加 range 驗證。
public class SystemConfig
{
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

    // ── 候選「退團率」可靠度信號（Feature 1b，預設關）──
    /// <summary>是否在候選卡顯示「退團率偏高」警示。預設關（名譽信號敏感）。</summary>
    public bool LeaveRateWarnEnabled { get; set; }
    /// <summary>退團率統計時間窗（月）。只看最近 N 個月的參加/退團。</summary>
    public int LeaveRateWindowMonths { get; set; } = 3;
    /// <summary>示警門檻（百分比）：退團率 ≥ 此值才警示。</summary>
    public int LeaveRateThreshold { get; set; } = 30;
    /// <summary>最小樣本數：窗內參加 < 此數不算率（避免 1/1=100% 小樣本誤判）。</summary>
    public int LeaveRateMinSample { get; set; } = 5;

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
