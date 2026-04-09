import { TeamSlot } from "@/types/raid";
import { apiClient } from './apiClient';

export const scheduleService = {
    async getTeamSlots(bossId: number): Promise<TeamSlot[]> {
        return apiClient.get<TeamSlot[]>(`/api/teamSlot?bossId=${bossId}`);
    },
    async autoSchedule(bossId: number, templateId: number): Promise<TeamSlot[]> {
        return apiClient.post<TeamSlot[]>("/api/schedule/AutoScheduleWithTemplate", { bossId, templateId });
    },
    async saveSchedule(bossId: number, teamSlots: TeamSlot[], deleteTeamSlotIds: number[]): Promise<void> {
        await apiClient.put("/api/teamSlot", { bossId, teamSlots, deleteTeamSlotIds });
    },
    async getByDiscordId(): Promise<TeamSlot[]> {
        return apiClient.get<TeamSlot[]>("/api/teamSlot/GetByDiscordId");
    }
};
