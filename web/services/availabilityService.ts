import { apiClient } from './apiClient';

export interface AvailabilityOverride {
    id: number;
    date: string;        // "YYYY-MM-DD"
    startTime: string;   // "HH:mm:ss"
    endTime: string;     // "HH:mm:ss"
    isAvailable: boolean;
}

export interface AvailabilityOverrideInput {
    date: string;
    startTime: string;
    endTime: string;
    isAvailable: boolean;
}

export const availabilityService = {
    getOverrides: (): Promise<AvailabilityOverride[]> =>
        apiClient.get<AvailabilityOverride[]>("/api/AvailabilityOverride"),
    addOverride: (o: AvailabilityOverrideInput): Promise<void> =>
        apiClient.post("/api/AvailabilityOverride", o),
    deleteOverride: (id: number): Promise<void> =>
        apiClient.delete(`/api/AvailabilityOverride/${id}`),
};
