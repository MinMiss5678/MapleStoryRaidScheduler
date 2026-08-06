import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";

export function useMyInvitations() {
    return useQuery({
        queryKey: ["myInvitations"],
        queryFn: () => leaderService.getMyInvitations(),
    });
}
