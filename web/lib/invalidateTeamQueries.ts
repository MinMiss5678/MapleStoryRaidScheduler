import { QueryClient } from "@tanstack/react-query";

// 隊伍相關的所有 query key——任何隊伍 mutation 後一次全失效，
// 避免各處 onSettled 漏 invalidate 某個 key 造成「要重新整理才更新」的整類 bug。
// 這些查詢彼此高度連動（開隊/入隊/轉讓/申請都會牽動多個列表與計數），
// 與其逐一精算該失效哪些，不如統一全刷——本專案資料量小，過度重取的成本可忽略。
const TEAM_QUERY_KEYS = [
    "ledTeams",
    "myTeams",
    "openTeams",
    "teamMembers",
    "leaderTransfers",
    "myInvitations",
    "applications",
    "candidates",
    "recruitmentGap",
    "lfgBoard",
] as const;

/** 失效所有隊伍相關查詢。於每個隊伍 mutation 的 onSettled 呼叫。 */
export function invalidateTeamQueries(qc: QueryClient) {
    for (const key of TEAM_QUERY_KEYS) qc.invalidateQueries({ queryKey: [key] });
}
