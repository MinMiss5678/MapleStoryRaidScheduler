import { useQuery } from "@tanstack/react-query";
import { leaderService } from "@/services/leaderService";
import { collaborativePolling } from "./collaborativePolling";

// 帶隊頁：`invitedCount`（已送出 N 則邀請）與已確認名冊會被玩家「DM 接受/婉拒」out-of-band 改動；
// 導覽回本頁時全域 60s staleTime 會擋掉重抓 → 顯示舊的「等待玩家回覆」。套 collaborativePolling
// （短 staleTime + focus 重抓 + 慢輪詢）讓它自動反映，不必手動重整。
export function useLedTeams() {
    return useQuery({
        queryKey: ["ledTeams"],
        queryFn: () => leaderService.getLedTeams(),
        ...collaborativePolling,
    });
}
