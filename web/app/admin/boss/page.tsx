"use client";

import { useState, useEffect } from "react";
import { Boss } from "@/types/raid";
import { Plus, Trash2, Save, Settings2, Shield } from "lucide-react";
import toast from "react-hot-toast";
import { bossService } from "@/services/bossService";

export default function BossAdminPage() {
    const [bosses, setBosses] = useState<Boss[]>([]);
    const [editingBoss, setEditingBoss] = useState<Boss | null>(null);

    useEffect(() => {
        loadBosses();
    }, []);

    const loadBosses = async () => {
        try {
            const data = await bossService.getAllBosses();
            setBosses(data);
        } catch {
            toast.error("無法取得 Boss 列表");
        }
    };

    const handleCreateBoss = () => {
        const newBoss: Boss = {
            id: 0,
            name: "新 Boss",
            requireMembers: 6,
            roundConsumption: 1
        };
        setEditingBoss(newBoss);
    };

    const handleSaveBoss = async () => {
        if (!editingBoss) return;
        
        try {
            const success = await bossService.saveBoss(editingBoss);
            if (success) {
                toast.success("Boss 已儲存");
                setEditingBoss(null);
                loadBosses();
            } else {
                toast.error("儲存失敗");
            }
        } catch {
            toast.error("儲存失敗");
        }
    };

    const handleDeleteBoss = async (id: number) => {
        if (!confirm("確定要刪除此 Boss 嗎？這可能會影響相關的範本與行程。")) return;
        try {
            const success = await bossService.deleteBoss(id);
            if (success) {
                toast.success("Boss 已刪除");
                loadBosses();
            } else {
                toast.error("刪除失敗");
            }
        } catch {
            toast.error("刪除失敗");
        }
    };

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground">
            <div className="max-w-4xl mx-auto">
                <div className="flex justify-between items-center mb-8">
                    <div>
                        <h1 className="text-3xl font-bold tracking-tight">Boss 管理</h1>
                        <p className="text-muted-foreground mt-1">管理可供排程的 Boss 及其基本屬性。</p>
                    </div>
                    <button
                        onClick={handleCreateBoss}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all shadow-md"
                    >
                        <Plus size={20} />
                        新增 Boss
                    </button>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                    {/* Boss 列表 */}
                    <div className="space-y-4">
                        <h2 className="text-xl font-bold flex items-center gap-2 mb-4">
                            <Shield size={20} />
                            Boss 列表
                        </h2>
                        {bosses.length === 0 && (
                            <div className="bg-card p-8 rounded-2xl border border-dashed border-border text-center text-muted-foreground">
                                尚無 Boss，請點擊右上方新增。
                            </div>
                        )}
                        {bosses.map((b) => (
                            <div
                                key={b.id}
                                className={`group p-4 rounded-2xl border transition-all cursor-pointer hover:shadow-md ${
                                    editingBoss?.id === b.id
                                        ? "border-blue-600 bg-blue-50/10"
                                        : "border-border bg-card hover:border-blue-400"
                                }`}
                                onClick={() => setEditingBoss(b)}
                            >
                                <div className="flex justify-between items-center">
                                    <div>
                                        <span className="font-bold text-lg">{b.name}</span>
                                        <div className="text-sm text-muted-foreground mt-1 flex gap-3">
                                            <span>需求人數: {b.requireMembers}</span>
                                            <span>消耗次數: {b.roundConsumption}</span>
                                        </div>
                                    </div>
                                    <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                        <button
                                            onClick={(e) => {
                                                e.stopPropagation();
                                                handleDeleteBoss(b.id);
                                            }}
                                            className="p-1.5 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg"
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>

                    {/* 編輯區域 */}
                    <div>
                        {editingBoss ? (
                            <div className="bg-card p-6 rounded-2xl border border-border shadow-sm sticky top-8">
                                <div className="flex justify-between items-center mb-6">
                                    <h2 className="text-xl font-bold">編輯 Boss: {editingBoss.name}</h2>
                                    <div className="flex gap-2">
                                        <button
                                            onClick={() => setEditingBoss(null)}
                                            className="px-4 py-2 text-muted-foreground hover:bg-muted rounded-xl transition-all"
                                        >
                                            取消
                                        </button>
                                        <button
                                            onClick={handleSaveBoss}
                                            className="flex items-center gap-2 px-6 py-2 bg-green-600 text-white rounded-xl hover:bg-green-700 transition-all shadow-md"
                                        >
                                            <Save size={18} />
                                            儲存
                                        </button>
                                    </div>
                                </div>

                                <div className="space-y-6">
                                    <div>
                                        <label className="text-sm font-medium mb-2 block">Boss 名稱</label>
                                        <input
                                            type="text"
                                            value={editingBoss.name}
                                            onChange={(e) => setEditingBoss({ ...editingBoss, name: e.target.value })}
                                            className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-blue-500 outline-none"
                                            placeholder="例如：賽蓮、卡洛斯"
                                        />
                                    </div>

                                    <div>
                                        <label className="text-sm font-medium mb-2 block">需求人數</label>
                                        <input
                                            type="number"
                                            value={editingBoss.requireMembers}
                                            onChange={(e) => setEditingBoss({ ...editingBoss, requireMembers: Number(e.target.value) })}
                                            className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-blue-500 outline-none"
                                            min={1}
                                            max={200}
                                        />
                                    </div>

                                    <div>
                                        <label className="text-sm font-medium mb-2 block">消耗次數</label>
                                        <input
                                            type="number"
                                            value={editingBoss.roundConsumption}
                                            onChange={(e) => setEditingBoss({ ...editingBoss, roundConsumption: Number(e.target.value) })}
                                            className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-blue-500 outline-none"
                                            min={0}
                                        />
                                        <p className="text-xs text-muted-foreground mt-1">此 Boss 會消耗角色的每週排程次數。</p>
                                    </div>
                                </div>
                            </div>
                        ) : (
                            <div className="bg-card h-full min-h-[400px] flex flex-col items-center justify-center rounded-2xl border border-dashed border-border text-muted-foreground">
                                <Settings2 size={48} className="mb-4 opacity-20" />
                                <p>請選擇左側 Boss 進行編輯，或新增一個 Boss。</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}
