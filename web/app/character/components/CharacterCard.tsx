"use client"

import React from 'react';
import { Character } from "@/types/character";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/FormControls";
import { Edit2, Trash2 } from "lucide-react";

interface CharacterCardProps {
    character: Character;
    onEdit: (character: Character) => void;
    onDelete: (id: string) => void;
}

export default function CharacterCard({ character, onEdit, onDelete }: CharacterCardProps) {
    return (
        <Card hoverable className="p-4 flex justify-between items-center group">
            <div className="flex flex-col">
                <div className="flex items-center gap-2">
                    <span className="font-bold text-lg">{character.name}</span>
                    <span className="text-xs px-2 py-0.5 bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 rounded-full font-medium">
                        {character.id}
                    </span>
                </div>
                <div className="flex items-center gap-4 mt-1 text-sm text-[var(--text-muted)]">
                    <span className="flex items-center gap-1">
                        <span className="opacity-70">職業:</span> {character.job}
                    </span>
                    <span className="flex items-center gap-1">
                        <span className="opacity-70">攻擊力:</span> {character.attackPower}
                    </span>
                </div>
            </div>
            <div className="flex gap-2">
                <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => onEdit(character)}
                    className="p-2 bg-yellow-500/10 hover:bg-yellow-500 text-yellow-600 hover:text-white"
                    title="修改"
                >
                    <Edit2 size={18} />
                </Button>
                <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => {
                        if (confirm(`確定要刪除角色 ${character.name} 嗎？`)) {
                            onDelete(character.id);
                        }
                    }}
                    className="p-2 bg-red-500/10 hover:bg-red-500 text-red-600 hover:text-white"
                    title="刪除"
                >
                    <Trash2 size={18} />
                </Button>
            </div>
        </Card>
    );
}
