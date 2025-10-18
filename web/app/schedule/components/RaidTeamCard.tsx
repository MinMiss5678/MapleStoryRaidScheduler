"use client";

import { useState } from "react";
import AddCharacterModal from "./AddCharacterModal";
import {formatDateTime} from "@/utils/dateTimeUtil";

type TeamSlotCharacter = {
    characterId: string;
    discordId: number;
    discordName: string;
    characterName: string;
    job: string;
    attackPower: number;
    rounds: number;
};

type TeamSlot = {
    id: number;
    bossId: number;
    slotDateTime: Date;
    characters: TeamSlotCharacter[];
    deleteCharacterIds: string[];
    isTemporary?: boolean;
}

type RaidTeamCardProps = {
    bossId: number;
    teamSlot: TeamSlot;
    onTeamSlotUpdate: (team: TeamSlot) => void;
    onTeamSlotDelete: (id: TeamSlot) => void;
    onAddCharacter: (teamSlot: TeamSlot, character: TeamSlotCharacter) => void;
};

export default function RaidTeamCard({bossId, teamSlot, onTeamSlotUpdate, onTeamSlotDelete, onAddCharacter }: RaidTeamCardProps) {
    const [isModalOpen, setModalOpen] = useState(false);
    
    const handleRemoveCharacter = (teamSlot: TeamSlot, characterId: string) => {
        if (!teamSlot.isTemporary) {
            teamSlot.deleteCharacterIds.push(characterId);
        }

        const updatedTeam = {
            ...teamSlot,
            characters: teamSlot.characters.filter((c) => c.characterId !== characterId),
        };
        onTeamSlotUpdate(updatedTeam);
    }

    return (
        <div className="relative bg-gray-800 p-5 rounded-xl shadow-lg hover:shadow-xl transition w-140">
            {/* 刪除按鈕在右上角 */}
            <button
                onClick={() => onTeamSlotDelete(teamSlot)}
                className="absolute top-4 right-3 bg-red-500 hover:bg-red-600 text-white rounded-full p-2 shadow-md transition"
                title="刪除團隊"
            >
                🗑️
            </button>
            <div className="flex justify-between items-center mb-3 pr-10"> {/* 保留右側空間 */}
                <h3 className="text-blue-400 font-bold">
                    {formatDateTime(teamSlot.slotDateTime)}
                </h3>

                <button
                    onClick={() => setModalOpen(true)}
                    disabled={teamSlot.characters.length >= 6}
                    className="px-3 py-1 bg-blue-600 rounded hover:bg-blue-700 disabled:bg-gray-600"
                >
                    新增角色
                </button>
            </div>

            {/* 成員清單 */}
            <div className="space-y-2">
                <div className="grid grid-cols-5 text-blue-300 font-semibold border-b border-gray-600 pb-1">
                    <span>Discord</span>
                    <span>角色</span>
                    <span>職業</span>
                    <span>攻擊力</span>
                    <span>操作</span>
                </div>
                {teamSlot.characters.map((m) => (
                    <div
                        key={m.characterId}
                        className="grid grid-cols-5 text-gray-300 border-b border-gray-700 pb-1 items-center"
                    >
                        <span>{m.discordName}</span>
                        <span>{m.characterName}</span>
                        <span>{m.job}</span>
                        <span>{m.attackPower}</span>
                        <button
                            onClick={() => handleRemoveCharacter(teamSlot, m.characterId)}
                            className="bg-red-500 hover:bg-red-600 text-white rounded px-2 py-1"
                        >
                            刪除
                        </button>
                    </div>
                ))}
            </div>

            <AddCharacterModal
                bossId={bossId}
                isOpen={isModalOpen}
                onClose={() => setModalOpen(false)}
                teamSlot={teamSlot}
                onAdd={onAddCharacter}
            />
        </div>
    );
}