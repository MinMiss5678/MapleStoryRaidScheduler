import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";
import { collaborativePolling } from "./collaborativePolling";

// 本隊招募缺口（還缺哪些職業幾位）——隊長挑候選時對照組成。
// 自動反映「別人接受 → Confirmed++ → 還缺 XX ×N」更新，隊長不必手動重整（設定說明見 collaborativePolling）。
export function useRecruitmentGap(teamSlotId: number) {
    return useQuery({
        queryKey: ["recruitmentGap", teamSlotId],
        queryFn: () => leaderService.getRecruitmentGap(teamSlotId),
        enabled: Number.isFinite(teamSlotId) && teamSlotId > 0,
        ...collaborativePolling,
    });
}
