namespace Domain.Entities;

// SystemConfig 同時是 admin POST 綁定型別；[Key] 對 Dapper 無作用故不加。
// period-less（Phase 4d）：報名截止相關欄位（DeadlineDayOfWeek/DeadlineTime/IsDeadlineNotified）已退場，
// 只剩候選「退團率」可靠度信號。
public class SystemConfig
{
    public int Id { get; set; }

    // ── 候選「退團率」可靠度信號（Feature 1b，預設關）──
    /// <summary>是否在候選卡顯示「退團率偏高」警示。預設關（名譽信號敏感）。</summary>
    public bool LeaveRateWarnEnabled { get; set; }
    /// <summary>退團率統計時間窗（月）。只看最近 N 個月的參加/退團。</summary>
    public int LeaveRateWindowMonths { get; set; } = 3;
    /// <summary>示警門檻（百分比）：退團率 ≥ 此值才警示。</summary>
    public int LeaveRateThreshold { get; set; } = 30;
    /// <summary>最小樣本數：窗內參加 < 此數不算率（避免 1/1=100% 小樣本誤判）。</summary>
    public int LeaveRateMinSample { get; set; } = 5;
}
