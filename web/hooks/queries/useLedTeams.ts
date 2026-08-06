import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useLedTeams() {
    return useQuery({
        queryKey: ["ledTeams"],
        queryFn: () => leaderService.getLedTeams(),
    });
}
