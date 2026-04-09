import { Boss, BossTemplate } from "@/types/raid";
import { apiClient } from './apiClient';

export const bossService = {
    async getAllBosses(): Promise<Boss[]> {
        return apiClient.get<Boss[]>("/api/boss/GetAll");
    },
    async getTemplates(bossId: number): Promise<BossTemplate[]> {
        return apiClient.get<BossTemplate[]>(`/api/boss/${bossId}/Templates`);
    },
    async createTemplate(template: BossTemplate): Promise<void> {
        await apiClient.post("/api/boss/Templates", template);
    },
    async updateTemplate(template: BossTemplate): Promise<void> {
        await apiClient.put(`/api/boss/Templates/${template.id}`, template);
    },
    async deleteTemplate(templateId: number): Promise<void> {
        await apiClient.delete(`/api/boss/Templates/${templateId}`);
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

export const jobCategoryService = {
    async getJobMap(): Promise<Record<string, string>> {
        return apiClient.get<Record<string, string>>("/api/JobCategory/GetJobMap");
    }
};
