import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";
import { collaborativePolling } from "./collaborativePolling";

// 候選池：自動反映「別人婉拒/退隊 → 位子重開、該人回到池子」，隊長不必手動重整就能繼續邀。
// （已邀/已入隊者由後端排除、不列；設定說明見 collaborativePolling。）
export function useCandidates(teamSlotId: number) {
    return useQuery({
        queryKey: ["candidates", teamSlotId],
        queryFn: () => leaderService.getCandidates(teamSlotId),
        enabled: Number.isFinite(teamSlotId) && teamSlotId > 0,
        ...collaborativePolling,
    });
}
