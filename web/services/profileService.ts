import { apiClient } from './apiClient';

export interface ProfileAvailability {
    weekday: number;     // ISO 1=一 … 7=日
    startTime: string;   // "HH:mm:ss"
    endTime: string;     // "HH:mm:ss"
}

export interface ProfileCharacter {
    id: string;
    name: string;
    job: string;
    attackPower: number;
    level: number;
    isSeekingRaid: boolean;
}

export interface Profile {
    availabilities: ProfileAvailability[];
    characters: ProfileCharacter[];
}

export interface ProfileSaveInput {
    availabilities: ProfileAvailability[];
    seekingCharacterIds: string[];
}

export const profileService = {
    getProfile: (): Promise<Profile> => apiClient.get<Profile>("/api/Profile"),
    saveProfile: (input: ProfileSaveInput): Promise<void> => apiClient.put("/api/Profile", input),
};
