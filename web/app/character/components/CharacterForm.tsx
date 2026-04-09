"use client"

import React, { useState, useEffect, useCallback } from 'react';
import { Character } from "@/types/character";
import { characterService } from '@/services/characterService';
import { Button, Input, Select } from '@/components/ui/FormControls';
import { Card, CardHeader, CardContent } from '@/components/ui/Card';

interface CharacterFormProps {
    editingCharacter: Character | null;
    onSuccess: (character: Character, isUpdate: boolean) => void;
    onReset: () => void;
    setLoading: (loading: boolean) => void;
    jobs: string[];
}

export default function CharacterForm({ editingCharacter, onSuccess, onReset, setLoading, jobs }: CharacterFormProps) {
    const [name, setName] = useState('');
    const [id, setId] = useState('');
    const [job, setJob] = useState(jobs[0] || '主教');
    const [attackPower, setAttackPower] = useState<number>(50);
    const [errors, setErrors] = useState<{ [k: string]: string }>({});

    const handleReset = useCallback(() => {
        setName('');
        setId('');
        setJob(jobs[0] || '主教');
        setAttackPower(50);
        setErrors({});
        onReset();
    }, [jobs, onReset]);

    useEffect(() => {
        if (editingCharacter) {
            setName(editingCharacter.name);
            setId(editingCharacter.id);
            setJob(editingCharacter.job);
            setAttackPower(editingCharacter.attackPower);
        } else {
            handleReset();
        }
    }, [editingCharacter, handleReset]);

    const onSubmit = async (e: React.FormEvent) => {
        setLoading(true);
        e.preventDefault();

        // 簡單驗證
        if (!name.trim() || !id.trim()) {
            setErrors({
                name: !name.trim() ? "請輸入名稱" : "",
                character: !id.trim() ? "請輸入代碼" : ""
            });
            setLoading(false);
            return;
        }

        try {
            const characterData = { name, id, job, attackPower };
            if (editingCharacter) {
                // 修改角色
                const updated = await characterService.updateCharacter({ ...characterData });
                onSuccess(updated, true);
            } else {
                // 建立角色
                const newCharacter = await characterService.createCharacter(characterData);
                onSuccess(newCharacter, false);
            }
            handleReset();
        } catch (error) {
            console.error(error);
            alert("操作失敗，請稍後再試");
        } finally {
            setLoading(false);
        }
    };

    return (
        <Card className="h-fit sticky top-6">
            <CardHeader>
                <h2 className="text-2xl font-semibold">{editingCharacter ? '修改角色' : '建立角色'}</h2>
            </CardHeader>
            <CardContent>
                <form onSubmit={onSubmit}>
                    <Input
                        label="角色名稱"
                        value={name}
                        maxLength={20}
                        onChange={(e) => setName(e.target.value)}
                        error={errors.name}
                        placeholder="例如：艾莉絲"
                        containerClassName="mb-4"
                    />

                    <Input
                        label="角色代碼"
                        value={id}
                        maxLength={5}
                        onChange={(e) => setId(e.target.value)}
                        disabled={!!editingCharacter}
                        error={errors.character}
                        placeholder="例如：ELISE"
                        containerClassName="mb-4"
                    />

                    <Select
                        label="職業"
                        value={job}
                        onChange={(e) => setJob(e.target.value)}
                        disabled={!!editingCharacter}
                        error={errors.job}
                        containerClassName="mb-4"
                    >
                        {jobs.map((j) => (
                            <option key={j} value={j} className="bg-[var(--background)] text-[var(--foreground)]">
                                {j}
                            </option>
                        ))}
                    </Select>
                    {editingCharacter && (
                        <p className="text-xs text-gray-500 mt-[-12px] mb-4">修改角色時不可更改職業</p>
                    )}

                    <Input
                        type="number"
                        label={`攻擊力: ${attackPower}`}
                        value={attackPower}
                        onChange={(e) => setAttackPower(Number(e.target.value))}
                        error={errors.atk}
                        containerClassName="mb-4"
                    />

                    <div className="flex items-center justify-between mt-6">
                        <Button type="submit">
                            提交
                        </Button>
                        <Button
                            type="button"
                            variant="outline"
                            onClick={handleReset}
                        >
                            重置
                        </Button>
                    </div>
                </form>
            </CardContent>
        </Card>
    );
}
