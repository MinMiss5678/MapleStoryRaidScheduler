"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Plus, Trash2, Clock } from "lucide-react";
import toast from "react-hot-toast";
import { availabilityService, AvailabilityOverrideInput } from "@/services/availabilityService";
import { ApiError } from "@/services/apiClient";

// 時間輸入 "HH:mm" → 後端 TimeOnly "HH:mm:ss"
const toApiTime = (t: string) => (t.length === 5 ? `${t}:00` : t);
const toInputTime = (t: string) => t.slice(0, 5);

export default function AvailabilityOverridePage() {
    const qc = useQueryClient();
    const { data: overrides = [], isLoading } = useQuery({
        queryKey: ["availabilityOverrides"],
        queryFn: () => availabilityService.getOverrides(),
    });

    const [date, setDate] = useState("");
    const [startTime, setStartTime] = useState("19:00");
    const [endTime, setEndTime] = useState("22:00");
    const [isAvailable, setIsAvailable] = useState(false);

    const add = useMutation({
        mutationFn: (o: AvailabilityOverrideInput) => availabilityService.addOverride(o),
        onSuccess: () => {
            toast.success("已新增例外");
            setDate("");
            qc.invalidateQueries({ queryKey: ["availabilityOverrides"] });
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "新增失敗，請稍後再試"),
    });

    const remove = useMutation({
        mutationFn: (id: number) => availabilityService.deleteOverride(id),
        onSuccess: () => qc.invalidateQueries({ queryKey: ["availabilityOverrides"] }),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "刪除失敗，請稍後再試"),
    });

    const submit = () => {
        if (!date) {
            toast.error("請選日期");
            return;
        }
        add.mutate({ date, startTime: toApiTime(startTime), endTime: toApiTime(endTime), isAvailable });
    };

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-sky-100 dark:bg-sky-900/30 text-sky-600 dark:text-sky-400 rounded-lg">
                    <CalendarClock size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">可用時段例外</h1>
                    <p className="text-sm text-muted-foreground">針對特定日期標「不行」或「額外加開」，蓋過你的常設時段。</p>
                </div>
            </div>

            {/* 新增表單 */}
            <div className="bg-card border border-border rounded-2xl p-5 shadow-sm mb-6 flex flex-col gap-3">
                <div className="flex flex-wrap gap-3">
                    <label className="flex flex-col gap-1 text-sm">
                        日期
                        <input type="date" value={date} onChange={(e) => setDate(e.target.value)}
                            className="border border-border rounded-lg px-3 py-2 bg-background" />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                        開始
                        <input type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)}
                            className="border border-border rounded-lg px-3 py-2 bg-background" />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                        結束
                        <input type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)}
                            className="border border-border rounded-lg px-3 py-2 bg-background" />
                    </label>
                </div>
                <div className="flex gap-2">
                    <button type="button" onClick={() => setIsAvailable(false)}
                        className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${!isAvailable ? "bg-red-600 text-white" : "bg-muted text-foreground"}`}>
                        不行（蓋掉常設）
                    </button>
                    <button type="button" onClick={() => setIsAvailable(true)}
                        className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${isAvailable ? "bg-green-600 text-white" : "bg-muted text-foreground"}`}>
                        額外加開
                    </button>
                    <button type="button" disabled={add.isPending} onClick={submit}
                        className="ml-auto px-4 py-2 bg-sky-600 text-white rounded-xl hover:bg-sky-700 disabled:opacity-50 flex items-center gap-1.5 font-medium">
                        <Plus size={16} /> 新增
                    </button>
                </div>
            </div>

            {/* 清單 */}
            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : overrides.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    還沒有任何例外。你的可用時段完全依常設。
                </div>
            ) : (
                <ul className="space-y-3">
                    {overrides.map((o) => (
                        <li key={o.id} className="bg-card border border-border rounded-2xl p-4 shadow-sm flex items-center gap-3">
                            <span className={`px-2 py-0.5 rounded-md text-xs font-medium ${o.isAvailable ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400" : "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400"}`}>
                                {o.isAvailable ? "加開" : "不行"}
                            </span>
                            <span className="font-medium">{o.date}</span>
                            <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                <Clock size={14} /> {toInputTime(o.startTime)}–{toInputTime(o.endTime)}
                            </span>
                            <button disabled={remove.isPending} onClick={() => remove.mutate(o.id)}
                                className="ml-auto p-2 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg disabled:opacity-50">
                                <Trash2 size={16} />
                            </button>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
