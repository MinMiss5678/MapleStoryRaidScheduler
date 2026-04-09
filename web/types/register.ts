export type Period = {
    id: number;
    startDate: Date;
    endDate: Date;
}

export type CharacterRegister = {
    id?: number;
    characterId: string | undefined;
    bossId: number | undefined;
    rounds: number;
}

export type Availability = {
    weekday: number;
    timeslot: string;
    startTime: string;
    endTime: string;
}

export interface RegisterFormState {
    id?: number;
    periodId: number | null;
    availabilities: Availability[];
    characterRegisters: CharacterRegister[];
    deleteCharacterRegisterIds: number[];
}
