import React, { useState, useEffect } from "react";
import { RegisterFormState } from "@/types/register";
import { TIMESLOTS, WEEKDAYS } from "@/constants/register";

interface TimePickerProps {
    form: RegisterFormState;
    quickFill: (type: "weekday_night" | "weekend_all" | "clear" | "invert" | "last_week") => void;
    copyDay: (fromWeekday: number) => void;
    toggleAvailability: (weekday: number, timeslot: string, mode?: "add" | "remove") => void;
    handleWeekdayAllCheck: (weekday: number, checked: boolean) => void;
    handleTimeslotAllCheck: (timeslot: string, checked: boolean) => void;
    onNext: () => void;
    onCancel?: () => void;
    hasId: boolean;
}

export const TimePicker: React.FC<TimePickerProps> = ({
    form,
    quickFill,
    copyDay,
    toggleAvailability,
    handleWeekdayAllCheck,
    handleTimeslotAllCheck,
    onNext,
    onCancel,
    hasId
}) => {
    const [isMouseDown, setIsMouseDown] = useState(false);
    const [dragMode, setDragMode] = useState<"add" | "remove" | null>(null);

    useEffect(() => {
        const handleMouseUp = () => {
            setIsMouseDown(false);
            setDragMode(null);
        };
        window.addEventListener("mouseup", handleMouseUp);
        return () => window.removeEventListener("mouseup", handleMouseUp);
    }, []);

    const handleMouseDown = (weekday: number, timeslot: string) => {
        setIsMouseDown(true);
        const exists = form.availabilities.some(a => a.weekday === weekday && a.timeslot === timeslot);
        const mode = exists ? "remove" : "add";
        setDragMode(mode);
        toggleAvailability(weekday, timeslot, mode);
    };

    const handleMouseEnter = (weekday: number, timeslot: string) => {
        if (isMouseDown && dragMode) {
            toggleAvailability(weekday, timeslot, dragMode);
        }
    };

    return (
        <div className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 border border-[var(--border-color)] mb-6">
            <h2 className="text-xl font-semibold mb-4">Step 1：選擇可出團時間</h2>

            <div className="flex flex-wrap gap-2 mb-4">
                <button onClick={() => quickFill("last_week")} className="px-3 py-1 text-sm bg-[var(--btn-blue-bg)] text-white hover:bg-[var(--btn-blue-hover)] rounded-full transition-colors">同上週報名</button>
                <button onClick={() => quickFill("weekday_night")} className="px-3 py-1 text-sm bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 hover:bg-gray-300 dark:hover:bg-gray-600 rounded-full transition-colors">平日晚上 (20-00)</button>
                <button onClick={() => quickFill("weekend_all")} className="px-3 py-1 text-sm bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 hover:bg-gray-300 dark:hover:bg-gray-600 rounded-full transition-colors">週末全天 (01-00)</button>
                <button onClick={() => quickFill("invert")} className="px-3 py-1 text-sm bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 hover:bg-gray-300 dark:hover:bg-gray-600 rounded-full transition-colors">負向選取</button>
                <button onClick={() => quickFill("clear")} className="px-3 py-1 text-sm bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400 hover:bg-red-200 dark:hover:bg-red-900/50 rounded-full transition-colors">清除全部</button>
            </div>

            <div className="overflow-x-auto">
                <table className="w-full text-center border-collapse select-none">
                    <thead>
                        <tr>
                            <th className="p-2 border border-[var(--border-color)] bg-[var(--background)] sticky left-0 z-10 min-w-[100px]">時段 \ 星期</th>
                            {WEEKDAYS.map((d, i) => (
                                <th key={i} className="p-2 border border-[var(--border-color)] bg-[var(--background)] min-w-[60px]">
                                    <div className="flex flex-col items-center">
                                        <span className="cursor-pointer hover:text-blue-500" title="點擊複製此日到其他天" onClick={() => { if (confirm(`是否複製週${d}的時段到其他天？`)) copyDay(i); }}>{d}</span>
                                        <input
                                            type="checkbox"
                                            checked={TIMESLOTS.every(t => form.availabilities.some(a => a.weekday === i && a.timeslot === t))}
                                            onChange={(e) => handleWeekdayAllCheck(i, e.target.checked)}
                                            className="mt-1"
                                        />
                                    </div>
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {TIMESLOTS.map(t => (
                            <tr key={t}>
                                <td className="p-2 border border-[var(--border-color)] font-medium sticky left-0 bg-[var(--background)] z-10">
                                    <div className="flex items-center justify-between">
                                        <input
                                            type="checkbox"
                                            checked={[0, 1, 2, 3, 4, 5, 6].every(w => form.availabilities.some(a => a.weekday === w && a.timeslot === t))}
                                            onChange={(e) => handleTimeslotAllCheck(t, e.target.checked)}
                                            className="mr-2"
                                        />
                                        {t}
                                    </div>
                                </td>
                                {WEEKDAYS.map((_, i) => (
                                    <td
                                        key={i}
                                        className={`p-2 border border-[var(--border-color)] cursor-pointer transition-colors ${form.availabilities.some(a => a.weekday === i && a.timeslot === t)
                                            ? "bg-blue-500 dark:bg-blue-600 shadow-inner"
                                            : "hover:bg-gray-100 dark:hover:bg-gray-800"
                                            }`}
                                        onMouseDown={() => handleMouseDown(i, t)}
                                        onMouseEnter={() => handleMouseEnter(i, t)}
                                    >
                                    </td>
                                ))}
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            <div className="mt-6 flex gap-2">
                <button
                    onClick={onNext}
                    disabled={form.availabilities.length === 0}
                    className="px-4 py-2 rounded-lg font-semibold shadow-md bg-[var(--btn-blue-bg)] text-white hover:bg-[var(--btn-blue-hover)] transition-colors duration-200 disabled:opacity-50"
                >
                    下一步
                </button>
                {hasId && onCancel &&
                    <button
                        onClick={onCancel}
                        className="px-4 py-2 rounded-lg font-semibold shadow-md bg-red-500 hover:bg-red-600 dark:bg-red-600 dark:hover:bg-red-700 text-white transition-colors duration-200"
                    >
                        取消報名
                    </button>
                }
            </div>
        </div>
    );
};
