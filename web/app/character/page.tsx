"use client";

import React, {useState, useEffect} from 'react';
import {useLoading} from "@/app/providers/LoadingContext";
import CharacterForm from "./components/CharacterForm";
import CharacterCard from "./components/CharacterCard";
import { characterService } from "@/services/characterService";
import { Character } from "@/types/character";
import { JOBS, JOBS_WITH_ALL } from "@/constants/jobs";
import { Input, Select } from "@/components/ui/FormControls";

export default function CharacterPage() {
    const { setLoading } = useLoading();
    const [characters, setCharacters] = useState<Character[]>([]);
    const [editingCharacter, setEditingCharacter] = useState<Character | null>(null);

    const [searchTerm, setSearchTerm] = useState('');
    const [selectedJob, setSelectedJob] = useState('全部');

    const filteredCharacters = characters.filter(c => {
        const matchesSearch = c.name.toLowerCase().includes(searchTerm.toLowerCase()) || 
                             c.id.toLowerCase().includes(searchTerm.toLowerCase());
        const matchesJob = selectedJob === '全部' || c.job === selectedJob;
        return matchesSearch && matchesJob;
    });

    // 取得角色列表
    useEffect(() => {
        const getList = async () => {
            setLoading(true);
            try {
                const data = await characterService.getCharacters();
                setCharacters(data);
            } catch (error) {
                console.error("無法取得角色列表:", error);
            } finally {
                setLoading(false);
            }
        }

        getList();
    }, [setLoading])

    const deleteCharacters = async (id: string) => {
        setLoading(true);
        try {
            const success = await characterService.deleteCharacter(id);
            if (success) {
                setCharacters(characters.filter(r => r.id !== id));
            } else {
                alert("刪除失敗");
            }
        } catch (error) {
            console.error("刪除角色時出錯:", error);
            alert("刪除失敗，請稍後再試");
        } finally {
            setLoading(false);
        }
    };

    const handleFormSuccess = (character: Character, isUpdate: boolean) => {
        if (isUpdate) {
            setCharacters(characters.map(r => (r.id === character.id ? character : r)));
        } else {
            setCharacters([...characters, character]);
        }
        setEditingCharacter(null);
    };

    return (
        <div className="min-h-screen flex items-start justify-center bg-[var(--background)] text-[var(--foreground)] p-6">
            <div className="w-full max-w-6xl grid grid-cols-1 md:grid-cols-2 gap-6">

                {/* 表單 */}
                <CharacterForm 
                    editingCharacter={editingCharacter} 
                    onSuccess={handleFormSuccess} 
                    onReset={() => setEditingCharacter(null)}
                    setLoading={setLoading}
                    jobs={JOBS}
                />

                {/* 列表 */}
                <div className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 flex flex-col border border-[var(--border-color)] h-fit max-h-[calc(100vh-100px)] overflow-hidden">
                    <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center mb-6 gap-4">
                        <h2 className="text-2xl font-semibold">角色列表 ({filteredCharacters.length})</h2>
                        
                        <div className="flex flex-col sm:flex-row gap-2 w-full sm:w-auto">
                            <Input
                                type="text"
                                placeholder="搜尋名稱或代碼..."
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                            />
                            <Select
                                value={selectedJob}
                                onChange={(e) => setSelectedJob(e.target.value)}
                            >
                                {JOBS_WITH_ALL.map(j => (
                                    <option key={j} value={j} className="bg-[var(--background)] text-[var(--foreground)]">
                                        {j}
                                    </option>
                                ))}
                            </Select>
                        </div>
                    </div>

                    <div className="overflow-y-auto pr-2 custom-scrollbar">
                        {filteredCharacters.length === 0 ? (
                            <div className="text-center py-10 text-[var(--text-muted)] italic">
                                沒有找到相符的角色
                            </div>
                        ) : (
                            <div className="grid grid-cols-1 gap-4">
                                {filteredCharacters.map(c => (
                                    <CharacterCard 
                                        key={c.id} 
                                        character={c} 
                                        onEdit={setEditingCharacter} 
                                        onDelete={deleteCharacters} 
                                    />
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}
