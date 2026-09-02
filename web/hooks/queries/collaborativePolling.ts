// 候選頁「自動反映他人 out-of-band 改動」的查詢設定，共用於 useCandidates（池子）與 useRecruitmentGap（缺口）。
//
// 隊長「自己的」邀請/核准等 mutation 已由 invalidateTeamQueries 於 onSettled 立即全刷，不靠這個。
// 這套專治「別人」的 out-of-band 改動——玩家在 Discord DM 按接受/婉拒：
//   接受 → recruitmentGap 的「還缺 XX ×N」變；婉拒/退隊 → 位子重開、該人回到 candidates 池子。
// 兩者都沒有任何前端 mutation 會 invalidate，只能靠這裡的 focus 重抓 + 慢輪詢自動補上，
// 否則隊長得手動重整才看得到、才能繼續邀其他缺的職業。
//
// 節奏依實測招募 ~5 分/次（隊長不盯著、離開做別的事、間歇回來瞄）：
//   staleTime 10s          → 覆蓋全域 60s，讓「切別的 App 再切回來」確實觸發 refetchOnWindowFocus 重抓
//   refetchInterval 30s     → 蓋「分頁開著、人偶爾瞄」（含雙螢幕副螢幕：visible 但無 focus）；每 2 分 ~4 次可忽略
//   refetchIntervalInBackground false → 分頁切到背景（去做別的事）就停輪、不空轉，回前景由 focus 補
export const collaborativePolling = {
    staleTime: 10_000,
    refetchOnWindowFocus: true,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
} as const;
