import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

// 本隊招募缺口（還缺哪些職業幾位）——隊長挑候選時對照組成。
export function useRecruitmentGap(teamSlotId: number) {
    return useQuery({
        queryKey: ["recruitmentGap", teamSlotId],
        queryFn: () => leaderService.getRecruitmentGap(teamSlotId),
        enabled: Number.isFinite(teamSlotId) && teamSlotId > 0,
    });
}
