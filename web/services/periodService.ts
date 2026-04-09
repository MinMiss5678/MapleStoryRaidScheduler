import { Period } from "@/types/register";

export const periodService = {
    async getByNow(): Promise<Period> {
        const res = await fetch("/api/period/GetByNow");
        if (!res.ok) throw new Error("無法取得目前週期");
        const data = await res.json();
        return {
            ...data,
            startDate: new Date(data.startDate),
            endDate: new Date(data.endDate),
        };
    }
};
