"use client";
import { useState, useEffect } from "react";
import { Save, ShieldCheck, AlertCircle } from "lucide-react";
import toast from "react-hot-toast";
import { useLoading } from "@/app/providers/LoadingContext";
import { systemConfigService } from "@/services/systemConfigService";
import { SystemConfig } from "@/types/system";

export default function AdminConfigPage() {
    const [config, setConfig] = useState<SystemConfig | null>(null);
    const { setLoading } = useLoading();

    useEffect(() => {
        const fetchConfig = async () => {
            setLoading(true);
            try {
                setConfig(await systemConfigService.getConfig());
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
            await systemConfigService.saveConfig(config);
            toast.success("系統設定已儲存");
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "發生錯誤");
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
                                <AlertCircle className="w-6 h-6" />
                            </div>
                            <div>
                                <h2 className="text-xl font-bold">候選「退團率」警示</h2>
                                <p className="text-sm text-muted-foreground">隊長挑候選時，對退團率偏高者顯示提醒。</p>
                            </div>
                        </div>

                        <div className="space-y-4">
                            <label className="flex items-center gap-2 text-sm font-medium">
                                <input
                                    type="checkbox"
                                    checked={config.leaveRateWarnEnabled}
                                    onChange={(e) => setConfig({ ...config, leaveRateWarnEnabled: e.target.checked })}
                                />
                                候選卡顯示「退團率偏高」警示（預設關）
                            </label>
                            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                                <label className="space-y-2 block">
                                    <span className="text-sm font-medium text-muted-foreground">時間窗（月）</span>
                                    <input
                                        type="number" min={1}
                                        className="w-full h-10 px-3 text-sm rounded-lg bg-[var(--background)] text-[var(--foreground)] border border-border focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.leaveRateWindowMonths}
                                        onChange={(e) => setConfig({ ...config, leaveRateWindowMonths: parseInt(e.target.value) || 0 })}
                                    />
                                </label>
                                <label className="space-y-2 block">
                                    <span className="text-sm font-medium text-muted-foreground">門檻率（%）</span>
                                    <input
                                        type="number" min={0} max={100}
                                        className="w-full h-10 px-3 text-sm rounded-lg bg-[var(--background)] text-[var(--foreground)] border border-border focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.leaveRateThreshold}
                                        onChange={(e) => setConfig({ ...config, leaveRateThreshold: parseInt(e.target.value) || 0 })}
                                    />
                                </label>
                                <label className="space-y-2 block">
                                    <span className="text-sm font-medium text-muted-foreground">最小樣本數</span>
                                    <input
                                        type="number" min={1}
                                        className="w-full h-10 px-3 text-sm rounded-lg bg-[var(--background)] text-[var(--foreground)] border border-border focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.leaveRateMinSample}
                                        onChange={(e) => setConfig({ ...config, leaveRateMinSample: parseInt(e.target.value) || 0 })}
                                    />
                                </label>
                            </div>

                            <div className="border-t border-border pt-4 mt-2">
                                <div className="mb-3">
                                    <h2 className="text-lg font-bold">常設可用時段新鮮度</h2>
                                    <p className="text-sm text-muted-foreground">玩家逾此天數無任何組隊動作（開團／申請／接受／編輯時段…）→ 其常設時段不再列入候選與招募熱力圖供給。</p>
                                </div>
                                <label className="space-y-2 block max-w-[12rem]">
                                    <span className="text-sm font-medium text-muted-foreground">新鮮度門檻（天）</span>
                                    <input
                                        type="number" min={1}
                                        className="w-full h-10 px-3 text-sm rounded-lg bg-[var(--background)] text-[var(--foreground)] border border-border focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                        value={config.availabilityFreshnessDays}
                                        onChange={(e) => setConfig({ ...config, availabilityFreshnessDays: parseInt(e.target.value) || 0 })}
                                    />
                                </label>
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
