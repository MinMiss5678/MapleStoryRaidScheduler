import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useOpenTeams() {
    return useQuery({
        queryKey: ["openTeams"],
        queryFn: () => leaderService.getOpenTeams(),
    });
}
