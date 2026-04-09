import { Period } from "@/types/register";
import { apiClient } from './apiClient';

export const periodService = {
    async getByNow(): Promise<Period> {
        const data = await apiClient.get<Record<string, unknown>>("/api/period/GetByNow");
        return {
            ...data,
            startDate: new Date(data.startDate as string),
            endDate: new Date(data.endDate as string),
        } as Period;
    }
};
