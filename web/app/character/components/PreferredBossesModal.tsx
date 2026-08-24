"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { X, Save, Star } from "lucide-react";
import toast from "react-hot-toast";
import { Character } from "@/types/character";
import { characterService } from "@/services/characterService";
import { ApiError } from "@/services/apiClient";
import { useBosses } from "@/hooks/queries/useBosses";

interface PreferredBossesModalProps {
    character: Character;
    onClose: () => void;
}

// 角色偏好王（複選）——隊長挑候選時「偏好本王」的角色會排前 + 標記（軟訊號，非硬篩）。
export default function PreferredBossesModal({ character, onClose }: PreferredBossesModalProps) {
    const qc = useQueryClient();
    const { data: bosses = [] } = useBosses();
    const { data: preferred, isLoading } = useQuery({
        queryKey: ["preferredBosses", character.id],
        queryFn: () => characterService.getPreferredBosses(character.id),
    });

    const [selected, setSelected] = useState<Set<number>>(new Set());

    useEffect(() => {
        if (!preferred) return;
        setSelected(new Set(preferred));
    }, [preferred]);

    const toggle = (bossId: number) =>
        setSelected((prev) => {
            const next = new Set(prev);
            if (next.has(bossId)) next.delete(bossId);
            else next.add(bossId);
            return next;
        });

    const save = useMutation({
        mutationFn: () => characterService.savePreferredBosses(character.id, [...selected]),
        onSuccess: () => {
            toast.success("已儲存偏好王");
            qc.invalidateQueries({ queryKey: ["preferredBosses", character.id] });
            onClose();
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "儲存失敗，請稍後再試"),
    });

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
            <div
                className="w-full max-w-md bg-[var(--card-bg)] border border-[var(--border-color)] rounded-2xl shadow-xl flex flex-col max-h-[85vh]"
                onClick={(e) => e.stopPropagation()}
            >
                <div className="flex items-center justify-between p-5 border-b border-[var(--border-color)]">
                    <div className="flex items-center gap-2">
                        <Star size={18} className="text-blue-600 dark:text-blue-400" />
                        <h2 className="font-bold text-lg">{character.name} 的偏好王</h2>
                    </div>
                    <button onClick={onClose} className="text-[var(--text-muted)] hover:text-red-500 transition-colors" aria-label="關閉">
                        <X size={20} />
                    </button>
                </div>

                <div className="p-5 overflow-y-auto custom-scrollbar">
                    {isLoading ? (
                        <p className="text-center text-[var(--text-muted)] py-8">載入中…</p>
                    ) : bosses.length === 0 ? (
                        <p className="text-center text-[var(--text-muted)] py-8">尚無 Boss 資料</p>
                    ) : (
                        <ul className="flex flex-col gap-2">
                            {bosses.map((b) => (
                                <li key={b.id}>
                                    <label className="flex items-center gap-3 px-3 py-2 rounded-xl border border-[var(--border-color)] cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-800/50">
                                        <input type="checkbox" checked={selected.has(b.id)} onChange={() => toggle(b.id)} className="w-4 h-4" />
                                        <span className="font-medium">{b.name}</span>
                                    </label>
                                </li>
                            ))}
                        </ul>
                    )}
                </div>

                <div className="flex justify-end gap-2 p-5 border-t border-[var(--border-color)]">
                    <button onClick={onClose} className="px-4 py-2 text-sm rounded-lg border border-[var(--border-color)] hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors">
                        取消
                    </button>
                    <button
                        disabled={save.isPending || bosses.length === 0}
                        onClick={() => save.mutate()}
                        className="flex items-center gap-1.5 px-5 py-2 text-sm font-medium rounded-lg bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
                    >
                        <Save size={16} /> {save.isPending ? "儲存中…" : "儲存"}
                    </button>
                </div>
            </div>
        </div>
    );
}
