"use client";

import { useState } from "react";
import { Plus, X } from "lucide-react";
import { ProfileAvailability } from "@/services/profileService";

const WEEKDAYS = [
    { iso: 1, label: "週一" }, { iso: 2, label: "週二" }, { iso: 3, label: "週三" },
    { iso: 4, label: "週四" }, { iso: 5, label: "週五" }, { iso: 6, label: "週六" }, { iso: 7, label: "週日" },
];

// 常用時段預設（純輸入模板；存進 DB 的是精確時段）
const PRESETS = [
    { label: "平日晚上", start: "19:00", end: "22:00" },
    { label: "假日下午", start: "14:00", end: "18:00" },
    { label: "整天", start: "00:00", end: "00:00" },
];

const toApi = (t: string) => (t.length === 5 ? `${t}:00` : t);
const toInput = (t: string) => t.slice(0, 5);

interface Props {
    availabilities: ProfileAvailability[];
    onChange: (next: ProfileAvailability[]) => void;
}

export function AvailabilityStandingEditor({ availabilities, onChange }: Props) {
    const [start, setStart] = useState("19:00");
    const [end, setEnd] = useState("22:00");

    const block = () => ({ startTime: toApi(start), endTime: toApi(end) });
    const exists = (weekday: number, b: { startTime: string; endTime: string }) =>
        availabilities.some(a => a.weekday === weekday && a.startTime === b.startTime && a.endTime === b.endTime);

    const stamp = (weekdays: number[]) => {
        const b = block();
        const toAdd = weekdays
            .filter(w => !exists(w, b))
            .map(w => ({ weekday: w, startTime: b.startTime, endTime: b.endTime }));
        if (toAdd.length) onChange([...availabilities, ...toAdd]);
    };

    const removeAt = (idx: number) => onChange(availabilities.filter((_, i) => i !== idx));

    return (
        <div className="flex flex-col gap-4">
            {/* 常用時段預設 + 自訂 */}
            <div className="flex flex-wrap items-end gap-2">
                {PRESETS.map(p => (
                    <button key={p.label} type="button"
                        onClick={() => { setStart(p.start); setEnd(p.end); }}
                        className="px-3 py-1.5 rounded-lg text-sm bg-muted hover:bg-muted/70 font-medium">
                        {p.label}
                    </button>
                ))}
                <label className="flex flex-col gap-1 text-xs text-muted-foreground">開始
                    <input type="time" value={start} onChange={e => setStart(e.target.value)}
                        className="border border-border rounded-lg px-2 py-1.5 bg-background text-sm" />
                </label>
                <label className="flex flex-col gap-1 text-xs text-muted-foreground">結束
                    <input type="time" value={end} onChange={e => setEnd(e.target.value)}
                        className="border border-border rounded-lg px-2 py-1.5 bg-background text-sm" />
                </label>
            </div>

            {/* 批次蓋章 */}
            <div className="flex flex-wrap gap-2">
                <button type="button" onClick={() => stamp([1, 2, 3, 4, 5])}
                    className="px-3 py-1.5 rounded-lg text-sm bg-sky-100 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 font-medium">
                    套用到平日（一~五）
                </button>
                <button type="button" onClick={() => stamp([6, 7])}
                    className="px-3 py-1.5 rounded-lg text-sm bg-sky-100 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 font-medium">
                    套用到週末（六日）
                </button>
                <button type="button" onClick={() => stamp([1, 2, 3, 4, 5, 6, 7])}
                    className="px-3 py-1.5 rounded-lg text-sm bg-sky-100 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 font-medium">
                    套用到每天
                </button>
            </div>

            {/* 週格子 */}
            <ul className="flex flex-col gap-2">
                {WEEKDAYS.map(w => {
                    const dayBlocks = availabilities
                        .map((a, i) => ({ a, i }))
                        .filter(x => x.a.weekday === w.iso);
                    return (
                        <li key={w.iso} className="flex items-center gap-2 flex-wrap border border-border rounded-xl px-3 py-2">
                            <span className="w-12 shrink-0 font-medium text-sm">{w.label}</span>
                            {dayBlocks.length === 0 && <span className="text-xs text-muted-foreground">—</span>}
                            {dayBlocks.map(({ a, i }) => (
                                <span key={i} className="flex items-center gap-1 px-2 py-0.5 rounded-md bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300 text-xs">
                                    {toInput(a.startTime)}–{toInput(a.endTime)}
                                    <button type="button" onClick={() => removeAt(i)} aria-label="移除">
                                        <X size={12} />
                                    </button>
                                </span>
                            ))}
                            <button type="button" onClick={() => stamp([w.iso])}
                                className="ml-auto flex items-center gap-0.5 px-2 py-1 rounded-md text-xs text-sky-600 dark:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/20">
                                <Plus size={12} /> 加入
                            </button>
                        </li>
                    );
                })}
            </ul>
        </div>
    );
}
