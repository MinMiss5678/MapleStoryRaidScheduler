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
    }
};
