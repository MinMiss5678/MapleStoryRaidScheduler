import { SystemConfig } from "@/types/system";
import { apiClient } from './apiClient';

export const systemConfigService = {
    async getConfig(): Promise<SystemConfig> {
        return apiClient.get<SystemConfig>("/api/SystemConfig");
    },
    async saveConfig(config: SystemConfig): Promise<void> {
        await apiClient.post("/api/SystemConfig", config);
    }
};
