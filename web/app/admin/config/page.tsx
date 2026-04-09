"use client";
import { useState, useEffect } from "react";
import { Save, Clock, ShieldCheck, AlertCircle } from "lucide-react";
import toast from "react-hot-toast";
import { useLoading } from "@/app/providers/LoadingContext";
import { systemConfigService } from "@/services/systemConfigService";
import { SystemConfig } from "@/types/system";

const DAYS_OF_WEEK = [
    { value: 0, label: "星期日" },
    { value: 1, label: "星期一" },
    { value: 2, label: "星期二" },
    { value: 3, label: "星期三" },
    { value: 4, label: "星期四" },
    { value: 5, label: "星期五" },
    { value: 6, label: "星期六" },
];

export default function AdminConfigPage() {
    const [config, setConfig] = useState<SystemConfig | null>(null);
    const { setLoading } = useLoading();

    useEffect(() => {
        const fetchConfig = async () => {
            setLoading(true);
            try {
                const data = await systemConfigService.getConfig();
                // Ensure deadlineTime is in HH:mm format if it's HH:mm:ss
                if (data.deadlineTime && data.deadlineTime.length > 5) {
                    data.deadlineTime = data.deadlineTime.slice(0, 5);
                }
                setConfig(data);
            } catch (error) {
                toast.error(error instanceof Error ? error.message : "發生錯誤");
            } finally {
                setLoading(false);
            }
        };

        fetchConfig();
    }, [setLoading]);

    const handleSave = async () => {
        if (!config) return;

        setLoading(true);
        try {
            const success = await systemConfigService.saveConfig(config);

            if (success) {
                toast.success("系統設定已儲存");
            } else {
                toast.error("儲存失敗");
            }
        } catch {
            toast.error("發生錯誤");
        } finally {
            setLoading(false);
        }
    };

    if (!config) return null;

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground transition-colors">
            <div className="max-w-4xl mx-auto">
                <div className="flex justify-between items-center mb-8">
                    <h1 className="text-3xl font-bold tracking-tight flex items-center gap-3">
                        <ShieldCheck className="w-8 h-8 text-blue-600 dark:text-blue-400" />
                        系統管理設定
                    </h1>
                </div>

                <div className="grid grid-cols-1 gap-8">
                    <div className="bg-card p-6 rounded-2xl shadow-sm border border-border">
                        <div className="flex items-center gap-3 mb-6">
                            <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 rounded-lg">
                                <Clock className="w-6 h-6" />
                            </div>
                            <div>
                                <h2 className="text-xl font-bold">每週報名截止時間</h2>
                                <p className="text-sm text-muted-foreground">設定每週固定截止報名的時間點。</p>
                            </div>
                        </div>

                        <div className="space-y-6">
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                                <div className="space-y-2">
                                    <label className="text-sm font-medium flex items-center gap-2 text-muted-foreground">
                                        截止星期
                                    </label>
                                    <select
                                        className="w-full p-3 rounded-xl border border-input bg-background focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.deadlineDayOfWeek}
                                        onChange={(e) => setConfig({ ...config, deadlineDayOfWeek: parseInt(e.target.value) })}
                                    >
                                        {DAYS_OF_WEEK.map((day) => (
                                            <option key={day.value} value={day.value}>
                                                {day.label}
                                            </option>
                                        ))}
                                    </select>
                                </div>
                                <div className="space-y-2">
                                    <label className="text-sm font-medium flex items-center gap-2 text-muted-foreground">
                                        截止時刻
                                    </label>
                                    <input
                                        type="time"
                                        className="w-full p-3 rounded-xl border border-input bg-background focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.deadlineTime}
                                        onChange={(e) => setConfig({ ...config, deadlineTime: e.target.value })}
                                    />
                                </div>
                            </div>

                            <div className="p-4 bg-amber-50 dark:bg-amber-950/20 border border-amber-100 dark:border-amber-900/10 border-l-4 border-l-amber-500/80 rounded-xl flex gap-3 shadow-sm">
                                <AlertCircle className="w-5 h-5 text-amber-600 dark:text-amber-500/80 shrink-0 mt-0.5" />
                                <div className="text-sm text-amber-800 dark:text-amber-200/90 leading-relaxed">
                                    <p className="font-semibold mb-1">重要提醒</p>
                                    <p>系統將根據此設定，在每週固定時間自動截止報名。一旦超過此時間，使用者將無法新增或修改報名資料。</p>
                                </div>
                            </div>

                            <div className="flex justify-end pt-4">
                                <button
                                    onClick={handleSave}
                                    className="flex items-center gap-2 px-8 py-3 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all shadow-lg shadow-blue-500/20 font-bold"
                                >
                                    <Save className="w-5 h-5" />
                                    儲存所有變更
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
