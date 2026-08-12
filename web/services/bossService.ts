import { Boss } from "@/types/raid";
import { apiClient } from './apiClient';

export const bossService = {
    async getAllBosses(): Promise<Boss[]> {
        return apiClient.get<Boss[]>("/api/boss/GetAll");
    },
    async createBoss(boss: Boss): Promise<void> {
        await apiClient.post("/api/boss", boss);
    },
    async updateBoss(boss: Boss): Promise<void> {
        await apiClient.put(`/api/boss/${boss.id}`, boss);
    },
    async deleteBoss(bossId: number): Promise<void> {
        await apiClient.delete(`/api/boss/${bossId}`);
    }
};
