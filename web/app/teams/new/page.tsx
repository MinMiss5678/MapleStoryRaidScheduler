"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Crown, Plus, Trash2, Swords, Bookmark, Star, X } from "lucide-react";
import { useBosses } from "@/hooks/queries/useBosses";
import { useCharacters } from "@/hooks/queries/useCharacters";
import toast from "react-hot-toast";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { ApiError } from "@/services/apiClient";
import { JOBS } from "@/constants/jobs";
import { CreateTeamRequirementInput } from "@/types/leaderLed";

const emptyRow = (): CreateTeamRequirementInput => ({ count: 1, minClearCount: 0, jobs: [] });

// 需求組合預設（取代舊 JobCategory「一鍵勾滿整組」）：純瀏覽器 localStorage，零後端、personal。
const PRESET_KEY = "teamNew.requirementPresets";
type Preset = { name: string; rows: CreateTeamRequirementInput[] };

function loadPresets(): Preset[] {
    if (typeof window === "undefined") return [];
    try {
        const raw = window.localStorage.getItem(PRESET_KEY);
        return raw ? (JSON.parse(raw) as Preset[]) : [];
    } catch {
        return [];
    }
}

function savePresets(presets: Preset[]) {
    window.localStorage.setItem(PRESET_KEY, JSON.stringify(presets));
}

// 深拷貝需求列（避免套用預設後改動污染 localStorage 內容）
const cloneRows = (rows: CreateTeamRequirementInput[]): CreateTeamRequirementInput[] =>
    rows.map((r) => ({ ...r, jobs: r.jobs.map((j) => ({ ...j })) }));

// 預設打王時段＝下一個 00/30 整半點（本地時間、datetime-local 格式）→ 分鐘一開始就落在 00/30，免手動滑
const nextRoundSlot = (): string => {
    const d = new Date();
    d.setSeconds(0, 0);
    d.setMinutes(d.getMinutes() < 30 ? 30 : 60); // <30 → 本時:30；>=30 → 次時:00
    const p = (n: number) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
};

export default function NewTeamPage() {
    const router = useRouter();
    const qc = useQueryClient();
    const { data: bosses = [] } = useBosses();
    const { data: characters = [] } = useCharacters();

    const [bossId, setBossId] = useState(0);
    const [slotDateTime, setSlotDateTime] = useState("");
    const [isInstant, setIsInstant] = useState(false);   // period-less §8 Phase 3：即時團
    const [description, setDescription] = useState("");
    const [rows, setRows] = useState<CreateTeamRequirementInput[]>([emptyRow()]);
    const [presets, setPresets] = useState<Preset[]>([]);
    // 隊長參戰：帶自己一隻角色下去打（佔 1 位、自動入隊）；沒角色或勾「只揪人」則不帶。
    const [leaderCharacterId, setLeaderCharacterId] = useState("");
    const [organizerOnly, setOrganizerOnly] = useState(false);

    useEffect(() => setPresets(loadPresets()), []);
    useEffect(() => setSlotDateTime(nextRoundSlot()), []);   // 開頁預設下一個 00/30 整半點

    const applyPreset = (p: Preset) => setRows(cloneRows(p.rows));

    const saveCurrentAsPreset = () => {
        const name = window.prompt("這組需求要存成什麼名字？（例：正常火山團）")?.trim();
        if (!name) return;
        const next = [...presets.filter((p) => p.name !== name), { name, rows: cloneRows(rows) }];
        setPresets(next);
        savePresets(next);
        toast.success(`已存為「${name}」`);
    };

    const deletePreset = (name: string) => {
        const next = presets.filter((p) => p.name !== name);
        setPresets(next);
        savePresets(next);
    };

    const updateRow = (i: number, patch: Partial<CreateTeamRequirementInput>) =>
        setRows((rs) => rs.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));

    const toggleJob = (i: number, job: string) =>
        setRows((rs) =>
            rs.map((r, idx) => {
                if (idx !== i) return r;
                const has = r.jobs.some((j) => j.job === job);
                return {
                    ...r,
                    jobs: has ? r.jobs.filter((j) => j.job !== job) : [...r.jobs, { job, minAttackPower: 0 }],
                };
            }),
        );

    const setJobAttack = (i: number, job: string, minAttackPower: number) =>
        setRows((rs) =>
            rs.map((r, idx) =>
                idx === i ? { ...r, jobs: r.jobs.map((j) => (j.job === job ? { ...j, minAttackPower } : j)) } : r,
            ),
        );

    const create = useMutation({
        mutationFn: () =>
            leaderService.createTeam({
                bossId,
                // 即時團：時間＝現在、不綁 period；排程團用選的時段
                slotDateTime: isInstant ? new Date().toISOString() : new Date(slotDateTime).toISOString(),
                kind: isInstant ? "Instant" : "Scheduled",
                description: description.trim() || undefined,
                // 帶自己下去打 → 佔 1 位自動入隊；勾「只揪人」則不帶
                leaderCharacterId: organizerOnly ? undefined : (leaderCharacterId || undefined),
                requirements: rows,
            }),
        onSuccess: () => router.push("/me/led-teams"),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "開隊失敗，請稍後再試"),
        // 開隊後把隊伍相關快取全失效——否則跳到「我開的隊」看到的是舊快取、要重整才有新隊。
        onSettled: () => invalidateTeamQueries(qc),
    });

    // 有角色又不是「只揪人」→ 必須選一隻要帶的角色（避免忘了把自己排進去）
    const needLeaderChar = !organizerOnly && characters.length > 0;
    const canSubmit = bossId > 0 && (isInstant || slotDateTime !== "") && rows.every((r) => r.count >= 1)
        && (!needLeaderChar || leaderCharacterId !== "");

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 rounded-lg">
                    <Crown size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">開隊</h1>
                    <p className="text-sm text-muted-foreground">選王、時段，並設定要哪些職業/攻擊/通關數。</p>
                </div>
            </div>

            <div className="flex flex-col gap-5">
                {/* 王 + 時段 */}
                <div className="bg-card border border-border rounded-2xl p-5 flex flex-col gap-4">
                    {/* 隊伍種類：排程 vs 即時 */}
                    <div className="flex gap-2">
                        <button type="button" onClick={() => setIsInstant(false)}
                            className={`flex-1 px-3 py-2 rounded-xl text-sm font-medium transition-colors ${!isInstant ? "bg-amber-600 text-white" : "bg-muted text-foreground"}`}>
                            排程團（約好時間）
                        </button>
                        <button type="button" onClick={() => setIsInstant(true)}
                            className={`flex-1 px-3 py-2 rounded-xl text-sm font-medium transition-colors ${isInstant ? "bg-rose-600 text-white" : "bg-muted text-foreground"}`}>
                            即時團（現在就打）
                        </button>
                    </div>

                    <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium flex items-center gap-1.5"><Swords size={14} /> 王</span>
                        <select
                            value={bossId}
                            onChange={(e) => setBossId(Number(e.target.value))}
                            className="px-3 py-2 bg-background border border-border rounded-xl text-sm"
                        >
                            <option value={0}>選擇王…</option>
                            {bosses.map((b) => (
                                <option key={b.id} value={b.id}>{b.name}</option>
                            ))}
                        </select>
                    </label>

                    {isInstant ? (
                        <p className="text-sm text-muted-foreground">即時團：時間為「現在」，候選來自即時揪團看板。</p>
                    ) : (
                        <label className="flex flex-col gap-1.5">
                            <span className="text-sm font-medium">打王時段</span>
                            <input
                                type="datetime-local"
                                value={slotDateTime}
                                onChange={(e) => setSlotDateTime(e.target.value)}
                                step={1800}
                                className="px-3 py-2 bg-background border border-border rounded-xl text-sm"
                            />
                        </label>
                    )}

                    <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium">隊伍說明（選填）</span>
                        <textarea
                            value={description}
                            onChange={(e) => setDescription(e.target.value)}
                            rows={2}
                            maxLength={500}
                            placeholder="例：楓葉祝福9、需自備 buff…"
                            className="px-3 py-2 bg-background border border-border rounded-xl text-sm resize-none"
                        />
                    </label>

                    {/* 隊長參戰：帶自己一隻角色下去打（佔 1 位、自動入隊） */}
                    <div className="flex flex-col gap-1.5 border-t border-border pt-3">
                        <span className="text-sm font-medium flex items-center gap-1.5"><Crown size={14} /> 我帶哪隻角色下去打</span>
                        {characters.length === 0 ? (
                            <p className="text-xs text-muted-foreground">你還沒有角色——到「角色管理」新增後才能帶自己下去打；目前只能純揪人。</p>
                        ) : organizerOnly ? (
                            <p className="text-xs text-muted-foreground">只揪人模式：你不佔位、只負責組隊與審核。</p>
                        ) : (
                            <select
                                value={leaderCharacterId}
                                onChange={(e) => setLeaderCharacterId(e.target.value)}
                                className="px-3 py-2 bg-background border border-border rounded-xl text-sm"
                            >
                                <option value="">選擇你要帶的角色…</option>
                                {characters.map((c) => (
                                    <option key={c.id} value={c.id}>{c.name}（{c.job}）</option>
                                ))}
                            </select>
                        )}
                        {characters.length > 0 && (
                            <label className="flex items-center gap-1.5 text-xs text-muted-foreground mt-0.5 cursor-pointer">
                                <input type="checkbox" checked={organizerOnly} onChange={(e) => setOrganizerOnly(e.target.checked)} />
                                我只揪人、自己不打（不佔位）
                            </label>
                        )}
                    </div>
                </div>

                {/* 條件 builder */}
                <div className="flex flex-col gap-3">
                    <div className="flex items-center justify-between">
                        <h2 className="font-semibold">隊伍條件</h2>
                        <button
                            onClick={() => setRows((rs) => [...rs, emptyRow()])}
                            className="text-sm px-3 py-1.5 border border-border rounded-lg hover:bg-muted transition-colors flex items-center gap-1"
                        >
                            <Plus size={14} /> 新增需求列
                        </button>
                    </div>

                    {/* 常用需求組合（localStorage）：一鍵套用取代舊職業分類快選 */}
                    <div className="flex flex-wrap items-center gap-1.5">
                        {presets.map((p) => (
                            <span key={p.name}
                                className="inline-flex items-center gap-1 text-xs pl-2.5 pr-1 py-1 bg-muted rounded-full">
                                <button onClick={() => applyPreset(p)} className="flex items-center gap-1 hover:text-amber-600 transition-colors">
                                    <Star size={11} /> {p.name}
                                </button>
                                <button onClick={() => deletePreset(p.name)} aria-label={`刪除 ${p.name}`}
                                    className="text-muted-foreground hover:text-red-500 transition-colors">
                                    <X size={12} />
                                </button>
                            </span>
                        ))}
                        <button onClick={saveCurrentAsPreset}
                            className="inline-flex items-center gap-1 text-xs px-2.5 py-1 border border-dashed border-border rounded-full hover:bg-muted transition-colors">
                            <Bookmark size={11} /> 存成常用
                        </button>
                    </div>

                    {rows.map((row, i) => (
                        <div key={i} className="bg-card border border-border rounded-2xl p-4 flex flex-col gap-3">
                            <div className="flex items-center gap-3">
                                <label className="flex items-center gap-1.5 text-sm">
                                    需要
                                    <input
                                        type="number"
                                        min={1}
                                        value={row.count}
                                        onChange={(e) => updateRow(i, { count: Number(e.target.value) })}
                                        className="w-16 px-2 py-1 bg-background border border-border rounded-lg text-sm"
                                    />
                                    位
                                </label>
                                <label className="flex items-center gap-1.5 text-sm">
                                    通關≥
                                    <input
                                        type="number"
                                        min={0}
                                        value={row.minClearCount}
                                        onChange={(e) => updateRow(i, { minClearCount: Number(e.target.value) })}
                                        className="w-16 px-2 py-1 bg-background border border-border rounded-lg text-sm"
                                    />
                                </label>
                                {rows.length > 1 && (
                                    <button
                                        onClick={() => setRows((rs) => rs.filter((_, idx) => idx !== i))}
                                        className="ml-auto text-muted-foreground hover:text-red-500 transition-colors"
                                        aria-label="移除需求列"
                                    >
                                        <Trash2 size={16} />
                                    </button>
                                )}
                            </div>

                            {/* 職業勾選 + 各自攻擊下限 */}
                            <div className="flex flex-wrap gap-2 border-t border-border pt-3">
                                {JOBS.map((job) => {
                                    const picked = row.jobs.find((j) => j.job === job);
                                    return (
                                        <div
                                            key={job}
                                            className={`flex items-center gap-1.5 px-2 py-1 rounded-lg border text-sm transition-colors ${
                                                picked ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20" : "border-border"
                                            }`}
                                        >
                                            <label className="flex items-center gap-1 cursor-pointer">
                                                <input
                                                    type="checkbox"
                                                    checked={!!picked}
                                                    onChange={() => toggleJob(i, job)}
                                                />
                                                {job}
                                            </label>
                                            {picked && (
                                                <input
                                                    type="number"
                                                    min={0}
                                                    value={picked.minAttackPower}
                                                    onChange={(e) => setJobAttack(i, job, Number(e.target.value))}
                                                    placeholder="攻擊≥"
                                                    className="w-20 px-1.5 py-0.5 bg-background border border-border rounded text-xs"
                                                />
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    ))}
                </div>

                <button
                    disabled={!canSubmit || create.isPending}
                    onClick={() => create.mutate()}
                    className="px-5 py-3 bg-amber-600 text-white rounded-xl hover:bg-amber-700 disabled:opacity-50 transition-colors font-medium flex items-center justify-center gap-2"
                >
                    <Crown size={18} /> {create.isPending ? "開隊中…" : "開隊"}
                </button>
            </div>
        </div>
    );
}
