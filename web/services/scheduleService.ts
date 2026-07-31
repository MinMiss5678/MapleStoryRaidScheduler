import { TeamSlot, TeamSlotSaveResult } from "@/types/raid";
import { apiClient } from './apiClient';

export const scheduleService = {
    async getTeamSlots(bossId: number): Promise<TeamSlot[]> {
        return apiClient.get<TeamSlot[]>(`/api/teamSlot?bossId=${bossId}`);
    },
    async autoSchedule(bossId: number, templateId: number): Promise<TeamSlot[]> {
        return apiClient.post<TeamSlot[]>("/api/schedule/AutoScheduleWithTemplate", { bossId, templateId });
    },
    async saveSchedule(bossId: number, teamSlots: TeamSlot[], deleteTeamSlotIds: number[]): Promise<TeamSlotSaveResult> {
        return apiClient.put<TeamSlotSaveResult>("/api/teamSlot", { bossId, teamSlots, deleteTeamSlotIds });
    },
    // 補位獨立端點：payload 型別上放不進別人的資料（DiscordId 一律用登入身分），
    // 不走 saveSchedule 那種整包重送的形狀，見 plans/2026-07-31-teamslot-fill-endpoint-separation.md。
    // 回傳值是後端寫入後重新查詢的最新隊伍（含新角色真實 id/version），呼叫端應該用這個更新畫面，
    // 不要自己在前端拼湊本地樂觀更新的資料。
    async fillSlot(fillRequest: {
        teamSlotId: number;
        discordName: string;
        characterId: string;
        characterName: string | null;
        job: string;
        attackPower: number;
        rounds: number;
    }): Promise<TeamSlot> {
        return apiClient.post<TeamSlot>("/api/teamSlot/Fill", fillRequest);
    },
    async getByDiscordId(): Promise<TeamSlot[]> {
        return apiClient.get<TeamSlot[]>("/api/teamSlot/GetByDiscordId");
    }
};
