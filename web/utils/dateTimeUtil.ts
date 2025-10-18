// utils/dateTimeUtil.ts

/**
 * 將日期轉換為在地時間或指定時區時間字串
 * @param date ISO 格式日期或 Date 物件
 * @param timeZone (可選) 指定時區，例如 "Asia/Taipei"
 * @param locale (可選) 指定語系，例如 "zh-TW"
 */
export function formatDateTime(
    date: string | Date,
    timeZone?: string,
    locale: string = "zh-TW"
): string {
    const dt = new Date(date);

    return dt.toLocaleString(locale, {
        timeZone, // 若不指定則使用使用者系統預設時區
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
    });
}

/**
 * 取得目前使用者系統所在的時區名稱
 * 例如："Asia/Taipei"、"America/Los_Angeles"
 */
export function getUserTimeZone(): string {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/**
 * 將星期幾與時段轉換為實際 DateTime（依指定週期）
 * @param startDate 打王週期的開始日期（Date 或字串）
 * @param weekday 星期幾（1=週一, 7=週日）
 * @param timeslot 時段字串，例如 "20:00"
 * @returns DateTimeOffset 對應的 ISO 字串（可送給後端）
 */
export function getDateTimeFromWeekday(
    startDate: string | Date,
    weekday: number,
    timeslot: string
): string {
    const start = new Date(startDate);
    const diffDays = (weekday - 1 + 7) % 7; // 確保 1=週一 對應週期第一天
    const target = new Date(start);
    target.setDate(start.getDate() + diffDays);

    const [hour, minute] = timeslot.split(":").map(Number);
    target.setHours(hour, minute, 0, 0);

    return target.toISOString();
}