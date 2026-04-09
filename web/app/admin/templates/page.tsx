"use client";

import { useState, useEffect } from "react";
import { Boss, BossTemplate, BossTemplateRequirement } from "@/types/raid";
import { Plus, Trash2, Save, ChevronRight, Settings2 } from "lucide-react";
import toast from "react-hot-toast";
import { bossService, jobCategoryService } from "@/services/bossService";
import { useBosses } from "@/hooks/queries/useBosses";

export default function TemplateAdminPage() {
    const { data: bosses = [] } = useBosses();
    const [selectedBoss, setSelectedBoss] = useState<Boss | null>(null);
    const [templates, setTemplates] = useState<BossTemplate[]>([]);
    const [editingTemplate, setEditingTemplate] = useState<BossTemplate | null>(null);

    const [jobCategories, setJobCategories] = useState<string[]>([]);
    const [jobs, setJobs] = useState<string[]>([]);

    useEffect(() => {
        async function loadJobData() {
            try {
                const jobMap = await jobCategoryService.getJobMap();
                const jobList = Object.keys(jobMap);
                const categoryList = Array.from(new Set(Object.values(jobMap)));
                setJobs(jobList);
                setJobCategories(categoryList);
            } catch {
                toast.error("無法取得職業資料");
            }
        }
        loadJobData();
    }, []);

    useEffect(() => {
        if (bosses.length > 0 && !selectedBoss) {
            setSelectedBoss(bosses[0]);
        }
    }, [bosses, selectedBoss]);

    useEffect(() => {
        if (selectedBoss) {
            loadTemplates(selectedBoss.id);
        }
    }, [selectedBoss]);

    const loadTemplates = async (bossId: number) => {
        try {
            const data = await bossService.getTemplates(bossId);
            setTemplates(data);
        } catch {
            toast.error("無法取得範本列表");
        }
    };

    const handleCreateTemplate = () => {
        if (!selectedBoss) return;
        const newTemplate: BossTemplate = {
            id: 0,
            bossId: selectedBoss.id,
            name: "新範本",
            requirements: []
        };
        setEditingTemplate(newTemplate);
    };

    const handleAddRequirement = () => {
        if (!editingTemplate) return;
        const newReq: BossTemplateRequirement = {
            bossTemplateId: editingTemplate.id,
            jobCategory: jobCategories.length > 0 ? jobCategories[0] : "",
            count: 1,
            priority: editingTemplate.requirements.length + 1
        };
        setEditingTemplate({
            ...editingTemplate,
            requirements: [...editingTemplate.requirements, newReq]
        });
    };

    const handleRemoveRequirement = (index: number) => {
        if (!editingTemplate) return;
        const newReqs = [...editingTemplate.requirements];
        newReqs.splice(index, 1);
        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
    };

    const handleSaveTemplate = async () => {
        if (!editingTemplate) return;
        
        try {
            const success = await bossService.saveTemplate(editingTemplate);
            if (success) {
                toast.success("範本已儲存");
                setEditingTemplate(null);
                if (selectedBoss) loadTemplates(selectedBoss.id);
            } else {
                toast.error("儲存失敗");
            }
        } catch {
            toast.error("儲存失敗");
        }
    };

    const handleDeleteTemplate = async (id: number) => {
        if (!confirm("確定要刪除此範本嗎？")) return;
        try {
            const success = await bossService.deleteTemplate(id);
            if (success) {
                toast.success("範本已刪除");
                if (selectedBoss) loadTemplates(selectedBoss.id);
            } else {
                toast.error("刪除失敗");
            }
        } catch {
            toast.error("刪除失敗");
        }
    };

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground">
            <div className="max-w-6xl mx-auto">
                <div className="flex justify-between items-center mb-8">
                    <div>
                        <h1 className="text-3xl font-bold tracking-tight">範本管理</h1>
                        <p className="text-muted-foreground mt-1">自定義不同 Boss 的團隊組成需求。</p>
                    </div>
                    <button
                        onClick={handleCreateTemplate}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all shadow-md"
                    >
                        <Plus size={20} />
                        新增範本
                    </button>
                </div>

                {/* Boss 選擇器 */}
                <div className="flex items-center gap-2 overflow-x-auto pb-4 no-scrollbar mb-8">
                    {bosses.map((boss) => (
                        <button
                            key={boss.id}
                            onClick={() => setSelectedBoss(boss)}
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

                <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
                    {/* 範本列表 */}
                    <div className="md:col-span-1 space-y-4">
                        <h2 className="text-xl font-bold flex items-center gap-2 mb-4">
                            <Settings2 size={20} />
                            範本列表
                        </h2>
                        {templates.length === 0 && (
                            <div className="bg-card p-8 rounded-2xl border border-dashed border-border text-center text-muted-foreground">
                                尚無範本，請點擊右上方新增。
                            </div>
                        )}
                        {templates.map((t) => (
                            <div
                                key={t.id}
                                className={`group p-4 rounded-2xl border transition-all cursor-pointer hover:shadow-md ${
                                    editingTemplate?.id === t.id
                                        ? "border-blue-600 bg-blue-50/10"
                                        : "border-border bg-card hover:border-blue-400"
                                }`}
                                onClick={() => setEditingTemplate(t)}
                            >
                                <div className="flex justify-between items-center">
                                    <span className="font-bold">{t.name}</span>
                                    <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                        <button
                                            onClick={(e) => {
                                                e.stopPropagation();
                                                handleDeleteTemplate(t.id);
                                            }}
                                            className="p-1.5 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg"
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                        <ChevronRight size={16} className="text-muted-foreground mt-1.5" />
                                    </div>
                                </div>
                                <div className="mt-2 flex flex-wrap gap-1">
                                    {t.requirements.map((r, idx) => (
                                        <span key={idx} className="text-[10px] px-1.5 py-0.5 bg-muted rounded-md border border-border">
                                            {r.jobCategory} x{r.count}
                                        </span>
                                    ))}
                                </div>
                            </div>
                        ))}
                    </div>

                    {/* 編輯區域 */}
                    <div className="md:col-span-2">
                        {editingTemplate ? (
                            <div className="bg-card p-6 rounded-2xl border border-border shadow-sm">
                                <div className="flex justify-between items-center mb-6">
                                    <h2 className="text-xl font-bold">編輯範本: {editingTemplate.name}</h2>
                                    <div className="flex gap-2">
                                        <button
                                            onClick={() => setEditingTemplate(null)}
                                            className="px-4 py-2 text-muted-foreground hover:bg-muted rounded-xl transition-all"
                                        >
                                            取消
                                        </button>
                                        <button
                                            onClick={handleSaveTemplate}
                                            className="flex items-center gap-2 px-6 py-2 bg-green-600 text-white rounded-xl hover:bg-green-700 transition-all shadow-md"
                                        >
                                            <Save size={18} />
                                            儲存
                                        </button>
                                    </div>
                                </div>

                                <div className="space-y-6">
                                    <div>
                                        <label className="text-sm font-medium mb-2 block">範本名稱</label>
                                        <input
                                            type="text"
                                            value={editingTemplate.name}
                                            onChange={(e) => setEditingTemplate({ ...editingTemplate, name: e.target.value })}
                                            className="w-full p-2.5 rounded-lg bg-background border border-border focus:ring-2 focus:ring-blue-500 outline-none"
                                            placeholder="例如：傳統團、拓荒團"
                                        />
                                    </div>

                                    <div>
                                        <div className="flex justify-between items-center mb-4">
                                            <label className="text-sm font-medium">職位需求</label>
                                            <button
                                                onClick={handleAddRequirement}
                                                className="text-sm flex items-center gap-1 text-blue-600 dark:text-blue-400 hover:underline"
                                            >
                                                <Plus size={14} /> 新增需求
                                            </button>
                                        </div>
                                        
                                        <div className="space-y-3">
                                            {editingTemplate.requirements.map((req, idx) => (
                                                <div key={idx} className="flex flex-col gap-3 p-4 bg-muted/30 rounded-xl border border-border">
                                                    <div className="flex gap-3 items-center">
                                                        <div className="flex-[2]">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">職業需求</span>
                                                                <select
                                                                    value={req.jobCategory}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].jobCategory = e.target.value;
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                >
                                                                    <optgroup label="職業類別">
                                                                        {jobCategories.map(cat => (
                                                                            <option key={cat} value={cat} className="bg-background dark:bg-zinc-800">{cat}</option>
                                                                        ))}
                                                                    </optgroup>
                                                                    <optgroup label="單一職業">
                                                                        {jobs.map(j => (
                                                                            <option key={j} value={j} className="bg-background dark:bg-zinc-800">{j}</option>
                                                                        ))}
                                                                    </optgroup>
                                                                    {!jobCategories.includes(req.jobCategory) && !jobs.includes(req.jobCategory) && req.jobCategory && (
                                                                        <optgroup label="自定義">
                                                                            <option value={req.jobCategory} className="bg-background dark:bg-zinc-800">{req.jobCategory}</option>
                                                                        </optgroup>
                                                                    )}
                                                                </select>
                                                            </div>
                                                        </div>
                                                        <div className="w-20">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">數量</span>
                                                                <input
                                                                    type="number"
                                                                    min={1}
                                                                    value={req.count}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].count = Number(e.target.value);
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-center text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                />
                                                            </div>
                                                        </div>
                                                        <div className="w-20">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">優先級</span>
                                                                <input
                                                                    type="number"
                                                                    value={req.priority}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].priority = Number(e.target.value);
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-center text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                />
                                                            </div>
                                                        </div>
                                                        <div className="flex items-center gap-2 mt-4">
                                                            <input
                                                                type="checkbox"
                                                                id={`opt-${idx}`}
                                                                checked={req.isOptional || false}
                                                                onChange={(e) => {
                                                                    const newReqs = [...editingTemplate.requirements];
                                                                    newReqs[idx].isOptional = e.target.checked;
                                                                    setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                }}
                                                                className="w-4 h-4 rounded border-border"
                                                            />
                                                            <label htmlFor={`opt-${idx}`} className="text-[10px] text-muted-foreground whitespace-nowrap">選配/盡量</label>
                                                        </div>
                                                        <button
                                                            onClick={() => handleRemoveRequirement(idx)}
                                                            className="p-2 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg mt-4"
                                                        >
                                                            <Trash2 size={16} />
                                                        </button>
                                                    </div>

                                                    <div className="flex gap-3 items-center border-t border-border pt-3">
                                                        <div className="w-24">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">最低等級</span>
                                                                <input
                                                                    type="number"
                                                                    value={req.minLevel || ""}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].minLevel = e.target.value ? Number(e.target.value) : undefined;
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-center text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                    placeholder="無"
                                                                />
                                                            </div>
                                                        </div>
                                                        <div className="w-24">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">最低 AP</span>
                                                                <input
                                                                    type="number"
                                                                    value={req.minAttribute || ""}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].minAttribute = e.target.value ? Number(e.target.value) : undefined;
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-center text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                    placeholder="無"
                                                                />
                                                            </div>
                                                        </div>
                                                        <div className="flex-1">
                                                            <div className="flex flex-col">
                                                                <span className="text-[10px] text-muted-foreground ml-1">備註/邏輯說明</span>
                                                                <input
                                                                    type="text"
                                                                    value={req.description || ""}
                                                                    onChange={(e) => {
                                                                        const newReqs = [...editingTemplate.requirements];
                                                                        newReqs[idx].description = e.target.value;
                                                                        setEditingTemplate({ ...editingTemplate, requirements: newReqs });
                                                                    }}
                                                                    className="w-full p-2 bg-background dark:bg-zinc-800 border border-border rounded-md text-sm text-foreground focus:ring-1 focus:ring-blue-500 outline-none"
                                                                    placeholder="例如：若等級 < 120 則需 2 位"
                                                                />
                                                            </div>
                                                        </div>
                                                    </div>
                                                </div>
                                            ))}
                                            {editingTemplate.requirements.length === 0 && (
                                                <div className="text-center py-4 text-muted-foreground italic text-sm">
                                                    尚未設定任何組成需求。
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            </div>
                        ) : (
                            <div className="bg-card h-full min-h-[400px] flex flex-col items-center justify-center rounded-2xl border border-dashed border-border text-muted-foreground">
                                <Settings2 size={48} className="mb-4 opacity-20" />
                                <p>請選擇左側範本進行編輯，或新增一個範本。</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}
