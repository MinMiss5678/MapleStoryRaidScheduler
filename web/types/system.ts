export interface SystemConfig {
    id: number;
    deadlineDayOfWeek: number;
    deadlineTime: string;
    // 候選「退團率」可靠度信號（Feature 1b）
    leaveRateWarnEnabled: boolean;
    leaveRateWindowMonths: number;
    leaveRateThreshold: number;   // 百分比：退團率 ≥ 此值才示警
    leaveRateMinSample: number;   // 窗內參加 < 此數不算率
    // 常設可用時段新鮮度（plans/2026-09-01-availability-freshness-decay.md）：逾此天數無組隊動作 → 常設時段不列入供給
    availabilityFreshnessDays: number;
}
