import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useCandidates(teamSlotId: number) {
    return useQuery({
        queryKey: ["candidates", teamSlotId],
        queryFn: () => leaderService.getCandidates(teamSlotId),
        enabled: Number.isFinite(teamSlotId) && teamSlotId > 0,
    });
}
