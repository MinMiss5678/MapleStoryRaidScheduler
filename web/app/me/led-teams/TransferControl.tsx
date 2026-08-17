"use client";

import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import toast from "react-hot-toast";
import { UserCog } from "lucide-react";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { ApiError } from "@/services/apiClient";

// 帶隊卡的「轉讓隊長」控制：展開後取本隊 Confirmed 名冊、選一個成員送出提議（需對方接受）。
export function TransferControl({ teamSlotId }: { teamSlotId: number }) {
    const qc = useQueryClient();
    const [open, setOpen] = useState(false);
    const [memberId, setMemberId] = useState("");

    const { data: roster = [] } = useQuery({
        queryKey: ["roster", teamSlotId],
        queryFn: () => leaderService.getTeamRoster(teamSlotId),
        enabled: open,
    });

    const propose = useMutation({
        mutationFn: (mid: number) => leaderService.proposeTransfer(teamSlotId, mid),
        onSuccess: () => {
            toast.success("已送出轉讓提議，等待對方接受");
            setOpen(false);
            setMemberId("");
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "轉讓失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
    });

    if (!open) {
        return (
            <button
                onClick={() => setOpen(true)}
                className="text-sm text-muted-foreground hover:text-foreground flex items-center gap-1 self-start"
            >
                <UserCog size={14} /> 轉讓隊長
            </button>
        );
    }

    return (
        <div className="flex gap-2 items-center">
            <select
                value={memberId}
                onChange={(e) => setMemberId(e.target.value)}
                className="flex-1 px-2 py-1.5 bg-background border border-border rounded-lg text-sm"
            >
                <option value="">選擇要轉讓給的成員…</option>
                {roster.map((m) => (
                    <option key={m.memberId} value={m.memberId}>
                        {m.discordName || m.characterName}
                    </option>
                ))}
            </select>
            <button
                disabled={!memberId || propose.isPending}
                onClick={() => propose.mutate(Number(memberId))}
                className="px-3 py-1.5 bg-amber-600 text-white rounded-lg text-sm hover:bg-amber-700 disabled:opacity-50 transition-colors"
            >
                送出
            </button>
            <button onClick={() => setOpen(false)} className="px-2 py-1.5 text-sm text-muted-foreground hover:text-foreground">
                取消
            </button>
        </div>
    );
}
