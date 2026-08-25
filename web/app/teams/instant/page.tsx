"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Zap, Radar, Trash2, Send, Swords } from "lucide-react";
import toast from "react-hot-toast";
import { lfgService } from "@/services/lfgService";
import { characterService } from "@/services/characterService";
import { bossService } from "@/services/bossService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { ApiError } from "@/services/apiClient";

export default function InstantLfgPage() {
    const qc = useQueryClient();
    // leader-led：只列「我掛著的找隊」（不再公開別人）。別人由隊長開即時團時在候選看到並邀。
    const { data: board = [], isLoading } = useQuery({
        queryKey: ["lfgBoard"],
        queryFn: () => lfgService.getBoard(),
    });
    const { data: characters = [] } = useQuery({ queryKey: ["myCharacters"], queryFn: () => characterService.getCharacters() });
    const { data: bosses = [] } = useQuery({ queryKey: ["bosses"], queryFn: () => bossService.getAllBosses() });

    const [characterId, setCharacterId] = useState("");
    const [bossId, setBossId] = useState<number | "">("");

    const post = useMutation({
        mutationFn: () => lfgService.post({ characterId, bossId: Number(bossId) }),
        onSuccess: () => toast.success("已發布找隊"),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "發布失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
    });

    const cancel = useMutation({
        mutationFn: (id: number) => lfgService.cancel(id),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "取消失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
    });

    const submit = () => {
        if (!characterId) {
            toast.error("請選角色");
            return;
        }
        if (bossId === "") {
            toast.error("請選王");
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
                    <h1 className="text-2xl font-bold">即時找隊</h1>
                    <p className="text-sm text-muted-foreground">掛上你現在想打的王，隊長開即時團時會在候選看到你、直接邀你入隊（約 3 小時後自動失效）。</p>
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
                        <option value="">選王…</option>
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
                    你目前沒有掛任何找隊。上面選角色與王發一個吧！
                </div>
            ) : (
                <>
                    <h2 className="text-sm font-semibold text-muted-foreground mb-3">我掛著的找隊（{board.length}）</h2>
                    <ul className="space-y-3">
                        {board.map((item) => (
                            <li key={item.id}
                                className="bg-card border border-rose-400 rounded-2xl p-4 shadow-sm flex items-center gap-3 flex-wrap">
                                <span className="font-medium">{item.characterName}</span>
                                <span className="text-sm text-muted-foreground">{item.job}</span>
                                <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                    <Swords size={14} /> {item.bossName}
                                </span>
                                <span className="ml-auto text-sm text-muted-foreground">Lv {item.level}</span>
                                <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                    <Zap size={14} /> {item.attackPower}
                                </span>
                                <button disabled={cancel.isPending} onClick={() => cancel.mutate(item.id)}
                                    className="p-2 text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/20 rounded-lg disabled:opacity-50">
                                    <Trash2 size={16} />
                                </button>
                            </li>
                        ))}
                    </ul>
                </>
            )}
        </div>
    );
}
