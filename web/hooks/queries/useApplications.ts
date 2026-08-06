import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useApplications(teamSlotId: number) {
    return useQuery({
        queryKey: ["applications", teamSlotId],
        queryFn: () => leaderService.getApplications(teamSlotId),
        enabled: Number.isFinite(teamSlotId) && teamSlotId > 0,
    });
}
