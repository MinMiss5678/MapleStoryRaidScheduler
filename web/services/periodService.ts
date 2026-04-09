import { Period } from "@/types/register";
import { apiClient } from './apiClient';

export const periodService = {
    async getByNow(): Promise<Period | null> {
        const data = await apiClient.getNullable<Record<string, unknown>>("/api/period/GetByNow");
        if (!data) return null;
        return {
            ...data,
            startDate: new Date(data.startDate as string),
            endDate: new Date(data.endDate as string),
        } as Period;
    }
};
