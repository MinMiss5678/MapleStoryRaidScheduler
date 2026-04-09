"use client";

import { useState } from "react";
import AddCharacterModal from "./AddCharacterModal";
import {formatDateTime} from "@/utils/dateTimeUtil";
import {TeamSlot, TeamSlotCharacter} from "@/types/raid";
import {Trash2, UserPlus, Users, UserMinus} from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/FormControls";

interface AdminRaidTeamCardProps {
    bossId: number;
    teamSlot: TeamSlot;
    onTeamSlotUpdate: (team: TeamSlot) => void;
    onTeamSlotDelete: (team: TeamSlot) => void;
    onAddCharacter: (teamSlot: TeamSlot, character: TeamSlotCharacter) => void;
}

export default function AdminRaidTeamCard({ bossId, teamSlot, onTeamSlotUpdate, onTeamSlotDelete, onAddCharacter }: AdminRaidTeamCardProps) {
    const [isModalOpen, setModalOpen] = useState(false);

    const joinedMembers = teamSlot.characters.filter(m => m.characterId !== null);
    const memberCount = joinedMembers.length;
    const isFull = memberCount >= 6;

    const handleRemoveCharacter = (id: number | undefined) => {
        if (id === undefined) return;
        const updatedTeam = {
            ...teamSlot,
            characters: teamSlot.characters.filter((c) => c.id !== id),
            deleteTeamSlotCharacterIds: !teamSlot.isTemporary 
                ? [...teamSlot.deleteTeamSlotCharacterIds, id]
                : teamSlot.deleteTeamSlotCharacterIds
        };
        onTeamSlotUpdate(updatedTeam);
    }

    return (
        <Card className={`p-6 ${
            isFull ? "border-green-500/50 dark:border-green-500/30 bg-green-50/10" : "border-border"
        }`}>
            {/* Header: DateTime & Actions */}
            <div className="flex justify-between items-start mb-4">
                <div className="space-y-1">
                    <div className="flex items-center gap-2">
                        <h3 className="text-lg font-bold text-blue-600 dark:text-blue-400">
                            {formatDateTime(teamSlot.slotDateTime)}
                        </h3>
                        {teamSlot.isTemporary && (
                            <span className="text-[10px] px-1.5 py-0.5 bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 border border-amber-200 dark:border-amber-800 rounded font-bold uppercase tracking-wider">
                                未儲存
                            </span>
                        )}
                    </div>
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                        <Users size={14} />
                        <span>成員: {memberCount} / 6</span>
                    </div>
                </div>

                <div className="flex gap-2">
                    <Button
                        size="sm"
                        variant="outline"
                        onClick={() => setModalOpen(true)}
                        disabled={isFull}
                        className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400"
                        title="新增成員"
                        leftIcon={<UserPlus size={18} />}
                    />
                    <Button
                        size="sm"
                        variant="outline"
                        onClick={() => onTeamSlotDelete(teamSlot)}
                        className="p-2 bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400 border-red-100 dark:border-red-900/30"
                        title="刪除隊伍"
                        leftIcon={<Trash2 size={18} />}
                    />
                </div>
            </div>

            {/* Members List */}
            <div className="overflow-hidden">
                <table className="w-full text-sm">
                    <thead>
                        <tr className="text-muted-foreground border-b border-border">
                            <th className="text-left font-medium py-2">玩家 / 角色</th>
                            <th className="text-left font-medium py-2 hidden sm:table-cell">職業</th>
                            <th className="text-right font-medium py-2">攻擊力</th>
                            <th className="text-right font-medium py-2 w-10"></th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                        {joinedMembers.map((m) => (
                            <tr key={m.characterId} className="group hover:bg-muted/30 transition-colors">
                                <td className="py-2.5">
                                    <div className="flex flex-col">
                                        <span className="font-medium">{m.characterName}</span>
                                        <span className="text-xs text-muted-foreground">{m.discordName}</span>
                                    </div>
                                </td>
                                <td className="py-2.5 hidden sm:table-cell">
                                    <span className="px-2 py-0.5 rounded text-xs bg-muted">
                                        {m.job}
                                    </span>
                                </td>
                                <td className="py-2.5 text-right font-mono text-xs">
                                    {m.attackPower.toLocaleString()}
                                </td>
                                <td className="py-2.5 text-right">
                                    <button
                                        onClick={() => handleRemoveCharacter(m.id)}
                                        className="text-muted-foreground hover:text-red-500 transition-colors p-1"
                                        title="移除成員"
                                    >
                                        <UserMinus size={16} />
                                    </button>
                                </td>
                            </tr>
                        ))}
                        {joinedMembers.length === 0 && (
                            <tr>
                                <td colSpan={4} className="py-8 text-center text-muted-foreground italic">
                                    尚無成員。
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>

            <AddCharacterModal
                bossId={bossId}
                isOpen={isModalOpen}
                onClose={() => setModalOpen(false)}
                teamSlot={teamSlot}
                onAdd={onAddCharacter}
            />
        </Card>
    );
}
