"use client"

import {useEffect, useState} from "react";
import RaidResultCard from "./components/RaidResultCard";

type TeamSlotCharacter = {
    characterId: string;
    discordId: number;
    discordName: string;
    characterName: string;
    job: string;
    attackPower: number;
}

type TeamSlot = {
    id: number;
    bossName: number;
    slotDateTime: Date;
    characters: TeamSlotCharacter[];
}

export default function RaidSchedulerResultPage() {
    const [teamSlots, setTeamSlots] = useState<TeamSlot[]>([]);

    useEffect(() => {
        async function loadTeamSlots() {
            const res = await fetch("/api/teamSlot/GetByDiscordId");
            if (res.ok) {
                const data = await res.json();
                setTeamSlots(data);
            }
        }

        loadTeamSlots()
    }, []);

    return (
        <div className="p-8 bg-gray-900 min-h-screen">
            <h1 className="text-2xl font-bold text-white mb-6">本期排團結果</h1>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
                {teamSlots.map((team) => (
                    <RaidResultCard key={team.id} teamSlot={team}/>
                ))}
            </div>
        </div>
    );
}