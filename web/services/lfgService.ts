import { apiClient } from './apiClient';

export interface LfgBoardItem {
    id: number;
    characterId: string;
    characterName: string;
    job: string;
    attackPower: number;
    bossId: number | null;
    bossName: string | null;
}

export interface LfgPostInput {
    characterId: string;
    bossId: number | null;
}

export const lfgService = {
    getBoard: (): Promise<LfgBoardItem[]> => apiClient.get<LfgBoardItem[]>("/api/LfgIntent"),
    post: (input: LfgPostInput): Promise<void> => apiClient.post("/api/LfgIntent", input),
    cancel: (id: number): Promise<void> => apiClient.delete(`/api/LfgIntent/${id}`),
};
