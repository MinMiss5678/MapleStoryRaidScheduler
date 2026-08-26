"use client";

import { useState } from "react";
import { leaderService } from "@/services/leaderService";
import { CreateTeamRequirementInput, RecruitmentHeatmap } from "@/types/leaderLed";

const WD = ["日", "一", "二", "三", "四", "五", "六"];

// 招募熱力圖（leader-recruitment-heatmap）：草稿需求 → 未來 14 天各整點的組成可填程度。
// 顏色越深＝越湊得齊這套組成；點格帶入開團時間（onPick 收 datetime-local 格式 YYYY-MM-DDTHH:mm）。
export function RecruitmentHeatmapPanel({ bossId, requirements, onPick }: {
    bossId: number;
    requirements: CreateTeamRequirementInput[];
    onPick: (localDateTime: string) => void;
}) {
    const [data, setData] = useState<RecruitmentHeatmap | null>(null);
    const [loading, setLoading] = useState(false);
    const [err, setErr] = useState("");

    const canLoad = bossId > 0 && requirements.some((r) => r.jobs.length > 0);

    const load = async () => {
        setLoading(true);
        setErr("");
        try {
            setData(await leaderService.getRecruitmentHeatmap({ bossId, days: 14, requirements }));
        } catch {
            setErr("熱力圖載入失敗");
        } finally {
            setLoading(false);
        }
    };

    const grid = data && data.cells.length > 0 ? (() => {
        const dates = Array.from(new Set(data.cells.map((c) => c.slotDateTime.slice(0, 10)))).sort();
        const hours = Array.from(new Set(data.cells.map((c) => Number(c.slotDateTime.slice(11, 13))))).sort((a, b) => a - b);
        const byKey = new Map(data.cells.map((c) => [`${c.slotDateTime.slice(0, 10)}|${Number(c.slotDateTime.slice(11, 13))}`, c]));
        return { dates, hours, byKey };
    })() : null;

    return (
        <div className="mt-4">
            <button
                type="button"
                disabled={!canLoad || loading}
                onClick={load}
                className="text-sm px-3 py-1.5 rounded-lg border border-border hover:bg-muted disabled:opacity-50"
            >
                {loading ? "載入中…" : "看熱力圖（挑最湊得起來的時段）"}
            </button>

            {err && <p className="text-sm text-red-500 mt-2">{err}</p>}
            {data && data.cells.length === 0 && <p className="text-sm text-muted-foreground mt-2">未來 14 天沒有合格候選的可用時段。</p>}

            {grid && data && (
                <div className="mt-3 overflow-x-auto">
                    <p className="text-xs text-muted-foreground mb-1">顏色越深＝該時段越能填滿這套組成（滿 = {data.totalRequired}）。點格帶入開團時間。</p>
                    <table className="border-collapse text-xs">
                        <thead>
                            <tr>
                                <th className="p-1 sticky left-0 bg-background" />
                                {grid.dates.map((d) => {
                                    const wd = new Date(`${d}T00:00:00+08:00`).getDay();
                                    return (
                                        <th key={d} className="p-1 font-normal text-muted-foreground whitespace-nowrap">
                                            {d.slice(5).replace("-", "/")}<br />週{WD[wd]}
                                        </th>
                                    );
                                })}
                            </tr>
                        </thead>
                        <tbody>
                            {grid.hours.map((h) => (
                                <tr key={h}>
                                    <td className="p-1 text-right text-muted-foreground sticky left-0 bg-background">{String(h).padStart(2, "0")}:00</td>
                                    {grid.dates.map((d) => {
                                        const cell = grid.byKey.get(`${d}|${h}`);
                                        if (!cell) return <td key={d} className="w-9 h-7" />;
                                        const ratio = data.totalRequired > 0 ? cell.filledCount / data.totalRequired : 0;
                                        return (
                                            <td key={d} className="p-0.5">
                                                <button
                                                    type="button"
                                                    title={`${cell.filledCount}/${data.totalRequired} 可填`}
                                                    onClick={() => onPick(cell.slotDateTime.slice(0, 16))}
                                                    className="w-9 h-7 rounded border border-border/50 hover:ring-2 hover:ring-blue-400 text-[10px] text-foreground/80"
                                                    style={{ backgroundColor: `rgba(34,197,94,${0.1 + ratio * 0.85})` }}
                                                >
                                                    {cell.filledCount || ""}
                                                </button>
                                            </td>
                                        );
                                    })}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}
