"use client";

import {useState, useEffect} from "react";
import AdminRaidTeamCard from "./components/AdminRaidTeamCard";
import {useLoading} from "@/app/providers/LoadingContext";
import {TeamSlot, TeamSlotCharacter, Boss, BossTemplate} from "@/types/raid";
import {useRaidValidation} from "@/hooks/useRaidValidation";
import {Calendar, Plus, BrainCircuit, Save} from "lucide-react";
import toast from "react-hot-toast";
import { bossService } from "@/services/bossService";
import { scheduleService } from "@/services/scheduleService";
import { useBosses } from "@/hooks/queries/useBosses";

export default function RaidSchedulerPage() {
    const { data: bosses = [] } = useBosses();
    const [selectedBoss, setSelectedBoss] = useState<Boss>();
    const [templates, setTemplates] = useState<BossTemplate[]>([]);
    const [selectedTemplate, setSelectedTemplate] = useState<number>(0);
    const [teamSlots, setTeamSlots] = useState<TeamSlot[]>([]);
    const [deleteTeamSlotIds, setDeleteTeamSlotIds] = useState<number[]>([]);
    const [originalTeamSlots, setOriginalTeamSlots] = useState<string>(""); // 用於檢索是否有變更
    const { setLoading } = useLoading();
    const { validateAddCharacter } = useRaidValidation();
    const [manualSlotDateTime, setManualSlotDateTime] = useState<string>(() => {
        const now = new Date();
        now.setMinutes(0, 0, 0); // 強制整點
        return now.toISOString().slice(0, 16); // YYYY-MM-DDTHH:MM
    });

    useEffect(() => {
        if (bosses.length > 0 && !selectedBoss) {
            setSelectedBoss(bosses[0]);
        }
    }, [bosses, selectedBoss]);

    useEffect(() => {
        async function loadTeamSlots() {
            if (!selectedBoss) return;
            try {
                const data = await scheduleService.getTeamSlots(selectedBoss.id);
                setTeamSlots(prev => {
                    const tempSlots = prev.filter(t => t.isTemporary && t.bossId === selectedBoss.id);
                    const allSlots = [...data, ...tempSlots];
                    setOriginalTeamSlots(JSON.stringify(data));
                    return allSlots;
                });
            } catch {
                toast.error("無法取得隊伍列表");
            }
        }

        async function loadTemplates() {
            if (!selectedBoss) return;
            try {
                const data = await bossService.getTemplates(selectedBoss.id);
                setTemplates(data);
                if (data.length > 0) {
                    setSelectedTemplate(data[0].id);
                } else {
                    setSelectedTemplate(0);
                }
            } catch {
                toast.error("無法取得範本列表");
            }
        }

        loadTeamSlots();
        loadTemplates();
    }, [selectedBoss]);

    const handleAutoSchedule = async () => {
        if (selectedTemplate <= 0 || !selectedBoss) {
            toast.error("請選擇一個排程範本");
            return;
        }

        try {
            const data = await scheduleService.autoSchedule(selectedBoss.id, selectedTemplate);
            // 在自動排程時，也保留手動新增的隊伍
            setTeamSlots(prev => {
                const tempSlots = prev.filter(t => t.isTemporary && t.bossId === selectedBoss?.id);
                return [...data, ...tempSlots];
            });
            toast.success("自動排團完成");
        } catch {
            toast.error("自動排團失敗");
        }
    };

    const handleAddManualTeamSlot = () => {
        if (!manualSlotDateTime || !selectedBoss) return;
        const [datePart, timePart] = manualSlotDateTime.split("T");
        const [hour] = timePart.split(":").map(Number);

        // 建立 Date 並強制整點
        const slotDate = new Date(datePart);
        slotDate.setHours(hour, 0, 0, 0);

        const newTeamSlot: TeamSlot = {
            id: -Date.now(),
            bossId: selectedBoss.id,
            slotDateTime: slotDate,
            characters: [],
            deleteTeamSlotCharacterIds: [],
            isTemporary: true,
        };

        setTeamSlots(prev => [...prev, newTeamSlot]);
        // setManualSlotDateTime(""); // 清空輸入，或保留方便連續新增
    };

    const handleConfirmSchedule = async () => {
        if (!selectedBoss || (teamSlots.length === 0 && deleteTeamSlotIds.length === 0)) return;
        setLoading(true);

        try {
            const success = await scheduleService.saveSchedule(selectedBoss.id, teamSlots, deleteTeamSlotIds);
            if (success) {
                // 重新載入隊伍列表以更新 ID 等資訊
                const data = await scheduleService.getTeamSlots(selectedBoss.id);
                toast.success("排團已儲存！");
                setDeleteTeamSlotIds([]);
                setTeamSlots(data);
                setOriginalTeamSlots(JSON.stringify(data));
            } else {
                toast.error("儲存失敗");
            }
        } catch {
            toast.error("儲存失敗");
        } finally {
            setLoading(false);
        }
    };

    const onTeamSlotUpdate = (updatedTeamSlot: TeamSlot) => {
        setTeamSlots(prev => prev.map(t => t.id === updatedTeamSlot.id ? { ...updatedTeamSlot } : t));
    };

    const onTeamSlotDelete = (teamSlot: TeamSlot) => {
        if (!teamSlot.isTemporary) {
            setDeleteTeamSlotIds(prev => [...prev, teamSlot.id]);
        }
        setTeamSlots((prev) => prev.filter((t) => t.id !== teamSlot.id));
    }

    const onAddCharacter = (teamSlot: TeamSlot, character: TeamSlotCharacter) => {
        const isValid = validateAddCharacter(teamSlot, character, teamSlots);
        
        if (!isValid) return;

        const updatedTeam = {
            ...teamSlot,
            characters: [...teamSlot.characters, character],
        };
        onTeamSlotUpdate(updatedTeam);
        toast.success(`已將 ${character.characterName} 加入隊伍`);
    };

    const hasChanges = originalTeamSlots !== JSON.stringify(teamSlots.filter(t => !t.isTemporary)) || 
                      teamSlots.some(t => t.isTemporary) || 
                      deleteTeamSlotIds.length > 0;

    return (
        <div className="relative min-h-screen p-4 md:p-8 bg-background text-foreground transition-colors">
            <div className="max-w-7xl mx-auto">
                <div className="flex justify-between items-center mb-8">
                    <h1 className="text-3xl font-bold tracking-tight">團隊排程</h1>
                </div>

                <div className="flex flex-col gap-6 mb-8">
                    <div className="flex items-center gap-2 overflow-x-auto pb-2 no-scrollbar">
                        {bosses.map((boss) => (
                            <button
                                key={boss.id}
                                onClick={() => {
                                    setSelectedBoss(boss);
                                }}
                                className={`px-4 py-2 rounded-full whitespace-nowrap transition-all font-medium border-2 ${
                                    selectedBoss?.id === boss.id
                                        ? "bg-blue-600 border-blue-600 text-white shadow-md shadow-blue-500/20"
                                        : "bg-card border-border text-muted-foreground hover:border-blue-400 hover:text-blue-500 dark:bg-zinc-900"
                                }`}
                            >
                                {boss.name}
                            </button>
                        ))}
                    </div>
                </div>

                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-10">
                    {/* 左側：自動排團區塊 */}
                    <div className="lg:col-span-2 bg-card p-6 rounded-2xl shadow-sm border border-border">
                        <div className="flex items-center gap-3 mb-6">
                            <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 rounded-lg">
                                <BrainCircuit size={24} />
                            </div>
                            <div>
                                <h2 className="text-xl font-bold flex items-center gap-2">
                                    🧠 自動排團
                                    {selectedBoss && (
                                        <span className="text-sm font-normal text-muted-foreground">
                                            (針對 {selectedBoss.name})
                                        </span>
                                    )}
                                </h2>
                                <p className="text-sm text-muted-foreground">設定條件，自動為 {selectedBoss?.name || "當前 Boss"} 產生最佳隊伍組合。</p>
                            </div>
                        </div>

                        <div className="flex flex-col md:flex-row gap-6 mb-6">
                            <div className="flex-1 space-y-4">
                                <div className="space-y-2">
                                    <label className="text-sm font-medium">排團模式</label>
                                    <select
                                        value={selectedTemplate}
                                        onChange={(e) => setSelectedTemplate(Number(e.target.value))}
                                        className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-blue-500 outline-none transition-all"
                                    >
                                        {templates.length === 0 && <option value={0}>無可用範本</option>}
                                        {templates.map(t => (
                                            <option key={t.id} value={t.id}>{t.name}</option>
                                        ))}
                                    </select>
                                </div>
                                
                                <div className="space-y-2">
                                    <label className="text-sm font-medium">範本需求</label>
                                    <div className="p-2.5 bg-muted rounded-lg border border-border text-xs flex flex-wrap gap-1 min-h-[46px] items-center">
                                        {selectedTemplate > 0 ? (
                                            templates.find(t => t.id === selectedTemplate)?.requirements.map((r, i) => (
                                                <span key={i} className="px-1.5 py-0.5 bg-background rounded border border-border shadow-sm">
                                                    {r.jobCategory} x{r.count}
                                                </span>
                                            ))
                                        ) : (
                                            <span className="text-muted-foreground italic">請選擇一個範本以查看詳細需求</span>
                                        )}
                                    </div>
                                </div>
                            </div>

                            <div className="flex items-end md:w-48">
                                <button
                                    onClick={handleAutoSchedule}
                                    className="w-full px-8 py-3 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all shadow-md hover:shadow-lg font-semibold flex items-center justify-center gap-2"
                                >
                                    <BrainCircuit size={18} />
                                    開始自動排團
                                </button>
                            </div>
                        </div>
                    </div>

                    {/* 右側：手動新增隊伍區塊 */}
                    <div className="bg-card p-6 rounded-2xl shadow-sm border border-border flex flex-col">
                        <div className="flex items-center gap-3 mb-6">
                            <div className="p-2 bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400 rounded-lg">
                                <Plus size={24} />
                            </div>
                            <div>
                                <h2 className="text-xl font-bold flex items-center gap-2">
                                    ➕ 手動新增
                                    {selectedBoss && (
                                        <span className="text-sm font-normal text-muted-foreground">
                                            ({selectedBoss.name})
                                        </span>
                                    )}
                                </h2>
                                <p className="text-sm text-muted-foreground">手動建立團隊。</p>
                            </div>
                        </div>

                        <div className="flex-1 flex flex-col justify-end">
                            <div className="space-y-4">
                                <button
                                    onClick={handleAddManualTeamSlot}
                                    className="w-full px-4 py-3 bg-purple-600 text-white rounded-xl hover:bg-purple-700 transition-all shadow-md hover:shadow-lg font-semibold flex items-center justify-center gap-2 h-[48px] whitespace-nowrap"
                                >
                                    <Plus size={18} />
                                    新增隊伍
                                </button>
                                <div className="space-y-2">
                                    <label className="text-sm font-medium flex items-center gap-2 text-muted-foreground">
                                        <Calendar size={14} />
                                        日期與時間
                                    </label>
                                    <input
                                        type="datetime-local"
                                        value={manualSlotDateTime}
                                        onChange={(e) => setManualSlotDateTime(e.target.value)}
                                        className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-purple-500 outline-none transition-all"
                                        step={3600}
                                    />
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                {/* 隊伍列表 */}
                {selectedBoss && (
                    <>
                        <div className="flex items-center justify-between mb-6">
                            <h2 className="text-2xl font-bold">隊伍列表</h2>
                            <span className="bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 px-3 py-1 rounded-full text-sm font-medium">
                                共 {teamSlots.length} 個隊伍
                            </span>
                        </div>
                        <div className="grid grid-cols-1 xl:grid-cols-2 2xl:grid-cols-3 gap-6 mb-24">
                            {teamSlots.map((teamSlot) => (
                                <AdminRaidTeamCard
                                    key={teamSlot.id}
                                    bossId={selectedBoss.id}
                                    teamSlot={teamSlot}
                                    onTeamSlotUpdate={onTeamSlotUpdate}
                                    onTeamSlotDelete={onTeamSlotDelete}
                                    onAddCharacter={onAddCharacter}
                                    requireMembers={selectedBoss.requireMembers}
                                />
                            ))}
                        </div>
                    </>
                )}
            </div>

            {/* 懸浮儲存按鈕 */}
            <div className="fixed bottom-8 right-8 flex flex-col items-end gap-4 z-50">
                {hasChanges && (
                    <div className="bg-amber-100 dark:bg-amber-900/40 text-amber-800 dark:text-amber-200 px-4 py-2 rounded-lg shadow-lg border border-amber-200 dark:border-amber-800 animate-bounce text-sm font-medium">
                        ⚠️ 有未儲存的變更
                    </div>
                )}
                <button
                    onClick={handleConfirmSchedule}
                    disabled={!hasChanges || (teamSlots.length === 0 && deleteTeamSlotIds.length === 0) || (teamSlots.length > 0 && teamSlots.every(t => t.characters.length === 0 && !t.isTemporary))}
                    className="flex items-center gap-2 px-6 py-4 bg-green-600 text-white rounded-full hover:bg-green-700 transition-all shadow-xl hover:shadow-2xl disabled:opacity-50 disabled:cursor-not-allowed transform hover:scale-105 active:scale-95 group"
                >
                    <Save size={24} className="group-hover:rotate-12 transition-transform" />
                    <span className="font-bold text-lg">儲存排程</span>
                </button>
            </div>
        </div>
    );
}