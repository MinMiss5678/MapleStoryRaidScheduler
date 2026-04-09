import { RegisterFormState } from "@/types/register";
import { TeamSlotCharacter } from "@/types/raid";
import { apiClient, ApiError } from './apiClient';

export const registerService = {
    async getRegister(): Promise<RegisterFormState | null> {
        try {
            return await apiClient.get<RegisterFormState>("/api/register");
        } catch (e) {
            if (e instanceof ApiError && (e.status === 404 || e.status === 204)) return null;
            throw e;
        }
    },
    async getLastRegister(): Promise<RegisterFormState | null> {
        try {
            return await apiClient.get<RegisterFormState>("/api/register/GetLast");
        } catch (e) {
            if (e instanceof ApiError && (e.status === 404 || e.status === 204)) return null;
            throw e;
        }
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
        try {
            return await apiClient.get<TeamSlotCharacter[]>(`/api/register/GetByQuery?${params}`);
        } catch (e) {
            if (e instanceof ApiError && (e.status === 404 || e.status === 204)) return null;
            throw e;
        }
    }
};
