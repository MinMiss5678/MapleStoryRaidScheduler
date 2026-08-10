export interface SystemConfig {
    id: number;
    deadlineDayOfWeek: number;
    deadlineTime: string;
    // 候選「退團率」可靠度信號（Feature 1b）
    leaveRateWarnEnabled: boolean;
    leaveRateWindowMonths: number;
    leaveRateThreshold: number;   // 百分比：退團率 ≥ 此值才示警
    leaveRateMinSample: number;   // 窗內參加 < 此數不算率
}
