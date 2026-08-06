import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useMyTeams() {
    return useQuery({
        queryKey: ["myTeams"],
        queryFn: () => leaderService.getMyTeams(),
    });
}
