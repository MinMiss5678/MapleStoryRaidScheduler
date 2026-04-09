export interface Character {
    id: string;
    name: string;
    job: string;
    attackPower: number;
    rounds?: number;
    registeredPeriodIds?: number[];
    discordId?: string;
    discordName?: string;
}
