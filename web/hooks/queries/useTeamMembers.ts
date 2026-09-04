import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";
import { collaborativePolling } from "./collaborativePolling";

// 本隊已確認組成（角色/職業/誰是隊長）——已入隊成員或隊長可查。
// 玩家 DM 接受 → 名冊多一人（out-of-band），套 collaborativePolling 讓帶隊頁名冊自動反映。
export function useTeamMembers(teamSlotId: number, enabled = true) {
    return useQuery({
        queryKey: ["teamMembers", teamSlotId],
        queryFn: () => leaderService.getTeamMembers(teamSlotId),
        enabled: enabled && Number.isFinite(teamSlotId) && teamSlotId > 0,
        ...collaborativePolling,
    });
}
