import { RegisterFormState } from "@/types/register";
import { TeamSlotCharacter } from "@/types/raid";
import { apiClient } from './apiClient';

export const registerService = {
    async getRegister(): Promise<RegisterFormState | null> {
        return apiClient.getNullable<RegisterFormState>("/api/register");
    },
    async getLastRegister(): Promise<RegisterFormState | null> {
        return apiClient.getNullable<RegisterFormState>("/api/register/GetLast");
    },
    async createRegister(form: RegisterFormState): Promise<void> {
        await apiClient.post("/api/register", form);
    },
    async updateRegister(form: RegisterFormState): Promise<void> {
        await apiClient.put("/api/register", form);
    },
    async deleteRegister(id: number): Promise<void> {
        await apiClient.delete(`/api/register/${id}`);
    },
    async getByQuery(params: string): Promise<TeamSlotCharacter[] | null> {
        return apiClient.getNullable<TeamSlotCharacter[]>(`/api/register/GetByQuery?${params}`);
    },
    // 目前開放報名週期的截止時間（後端算好的權威值，與擋報名同一套；沒有 active period 回 null）。
    // 前端只顯示、不自行用日曆週重算，避免前後端算法分歧誤報「已截止」。
    async getDeadline(): Promise<string | null> {
        const res = await apiClient.get<{ deadline: string | null }>("/api/register/Deadline");
        return res?.deadline ?? null;
    }
};
