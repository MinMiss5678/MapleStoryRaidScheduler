import { apiClient } from './apiClient';

export const authService = {
    async login(code: string): Promise<{ role: string }> {
        return apiClient.post<{ role: string }>("/api/auth/login", { code });
    },
    async logout(): Promise<void> {
        await apiClient.post("/api/auth/logout");
    },
};
