"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Zap, Radar, Trash2, Send, Swords } from "lucide-react";
import toast from "react-hot-toast";
import { lfgService } from "@/services/lfgService";
import { characterService } from "@/services/characterService";
import { bossService } from "@/services/bossService";
import { ApiError } from "@/services/apiClient";

export default function InstantLfgPage() {
    const qc = useQueryClient();
    // 即時看板：定時刷新製造「即時」感
    const { data: board = [], isLoading } = useQuery({
        queryKey: ["lfgBoard"],
        queryFn: () => lfgService.getBoard(),
        refetchInterval: 10000,
    });
    const { data: characters = [] } = useQuery({ queryKey: ["myCharacters"], queryFn: () => characterService.getCharacters() });
    const { data: bosses = [] } = useQuery({ queryKey: ["bosses"], queryFn: () => bossService.getAllBosses() });

    const [characterId, setCharacterId] = useState("");
    const [bossId, setBossId] = useState<number | "">("");

    const post = useMutation({
        mutationFn: () => lfgService.post({ characterId, bossId: bossId === "" ? null : Number(bossId) }),
        onSuccess: () => {
            toast.success("已發布找隊");
            qc.invalidateQueries({ queryKey: ["lfgBoard"] });
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "發布失敗，請稍後再試"),
    });

    const cancel = useMutation({
        mutationFn: (id: number) => lfgService.cancel(id),
        onSuccess: () => qc.invalidateQueries({ queryKey: ["lfgBoard"] }),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "取消失敗，請稍後再試"),
    });

    const submit = () => {
        if (!characterId) {
            toast.error("請選角色");
            return;
        }
        post.mutate();
    };

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-rose-100 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400 rounded-lg">
                    <Radar size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">即時揪團</h1>
                    <p className="text-sm text-muted-foreground">現在想打就發個找隊訊號，隊長開即時隊時看得到你（約 3 小時後自動失效）。</p>
                </div>
            </div>

            {/* 發布找隊 */}
            <div className="bg-card border border-border rounded-2xl p-5 shadow-sm mb-6 flex flex-wrap items-end gap-3">
                <label className="flex flex-col gap-1 text-sm flex-1 min-w-40">
                    角色
                    <select value={characterId} onChange={(e) => setCharacterId(e.target.value)}
                        className="border border-border rounded-lg px-3 py-2 bg-background">
                        <option value="">選角色…</option>
                        {characters.map((c) => (
                            <option key={c.id} value={c.id}>{c.name}（{c.job}）</option>
                        ))}
                    </select>
                </label>
                <label className="flex flex-col gap-1 text-sm flex-1 min-w-40">
                    想打的王
                    <select value={bossId} onChange={(e) => setBossId(e.target.value === "" ? "" : Number(e.target.value))}
                        className="border border-border rounded-lg px-3 py-2 bg-background">
                        <option value="">任意王</option>
                        {bosses.map((b) => (
                            <option key={b.id} value={b.id}>{b.name}</option>
                        ))}
                    </select>
                </label>
                <button type="button" disabled={post.isPending} onClick={submit}
                    className="px-4 py-2 bg-rose-600 text-white rounded-xl hover:bg-rose-700 disabled:opacity-50 flex items-center gap-1.5 font-medium">
                    <Send size={16} /> 我要找隊
                </button>
            </div>

            {/* 看板 */}
            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : board.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    目前沒有人在找隊。第一個發布吧！
                </div>
            ) : (
                <ul className="space-y-3">
                    {board.map((item) => (
                        <li key={item.id}
                            className={`bg-card border rounded-2xl p-4 shadow-sm flex items-center gap-3 flex-wrap ${item.isMine ? "border-rose-400" : "border-border"}`}>
                            <span className="font-medium">{item.characterName}</span>
                            <span className="text-sm text-muted-foreground">{item.job}</span>
                            <span className="text-xs text-muted-foreground">@{item.discordName}</span>
                            <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                <Swords size={14} /> {item.bossName ?? "任意王"}
                            </span>
                            <span className="ml-auto flex items-center gap-1 text-sm text-muted-foreground">
                                <Zap size={14} /> {item.attackPower}
                            </span>
                            {item.isMine && (
                                <button disabled={cancel.isPending} onClick={() => cancel.mutate(item.id)}
                                    className="p-2 text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/20 rounded-lg disabled:opacity-50">
                                    <Trash2 size={16} />
                                </button>
                            )}
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
