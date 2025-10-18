"use client"

import React, {useState, useEffect} from 'react';
import {useLoading} from "@/app/providers/LoadingContext";

type Character = {
    id: string;
    name: string;
    job: string;
    attackPower: number;
};

export default function CharacterPage() {
    const { setLoading } = useLoading();
    const [characters, setCharacters] = useState<Character[]>([]);
    const [editingCharacter, setEditingCharacter] = useState<Character | null>(null);
    const [name, setName] = useState('');
    const [id, setId] = useState('');
    const [job, setJob] = useState('祭司');
    const [attackPower, setAttackPower] = useState<number>(50);
    const [errors, setErrors] = useState<{ [k: string]: string }>({});

    const jobs = ['祭司', '火毒魔導士', '冰雷魔導士', '狙擊手', '十字軍', '騎士', '龍騎士', '神偷', '暗殺者', '格鬥家', '神槍手'];

    // 取得角色列表
    useEffect(() => {
        const getList = async () => {
            const res = await fetch("/api/character/GetByDiscordId");
            const data = await res.json();
            setCharacters(data);
        }

        getList();
    }, [])

    const deleteCharacters = async (id: string) => {
        setLoading(true);
        const encodedId = encodeURIComponent(id);
        await fetch(`/api/character/${encodedId}`, {method: "DELETE"});
        setCharacters(characters.filter(r => r.id !== id));
        setLoading(false);
    };

    const onSubmit = async (e: React.FormEvent) => {
        setLoading(true);
        e.preventDefault();

        if (editingCharacter) {
            // 修改角色
            const res = await fetch(`/api/character/${editingCharacter.id}`, {
                method: "PUT",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify({name, id, job, attackPower}),
            });
            const updated: Character = await res.json();
            setCharacters(characters.map(r => (r.id === updated.id ? updated : r)));
            setEditingCharacter(null);
        } else {
            // 建立角色
            const res = await fetch("/api/character", {
                method: "POST",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify({name, id, job, attackPower}),
            });
            const newCharacter = await res.json();
            setCharacters([...characters, newCharacter]);
        }

        setName("");
        setId("");
        setJob(jobs[0]);
        setAttackPower(50);
        setLoading(false);
    };

    const editCharacters = (character: Character) => {
        setEditingCharacter(character);
        setName(character.name);
        setId(character.id);
        setJob(character.job);
        setAttackPower(character.attackPower);
    };

    return (
        <div
            className="min-h-screen flex items-start justify-center bg-[var(--background)] text-[var(--foreground)] p-6">
            <div className="w-full max-w-6xl grid grid-cols-1 md:grid-cols-2 gap-6">

                {/* 表單 */}
                <form
                    onSubmit={onSubmit}
                    className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 border border-[var(--border-color)]"
                >
                    <h2 className="text-2xl font-semibold mb-4">建立角色</h2>

                    <div className="mb-4">
                        <label className="block text-sm font-medium mb-1">角色名稱</label>
                        <input
                            value={name}
                            maxLength={20}
                            onChange={(e) => setName(e.target.value)}
                            className={`w-full border rounded px-3 py-2 focus:outline-none focus:ring bg-[var(--card-bg)] text-[var(--foreground)] ${
                                errors.name ? 'border-red-400' : 'border-[var(--border-color)]'
                            }`}
                            placeholder="例如：艾莉絲"
                        />
                        {errors.name && <p className="text-xs text-red-400 mt-1">{errors.name}</p>}
                    </div>

                    <div className="mb-4">
                        <label className="block text-sm font-medium mb-1">角色代碼</label>
                        <input
                            value={id}
                            maxLength={5}
                            onChange={(e) => setId(e.target.value)}
                            className={`w-full border rounded px-3 py-2 focus:outline-none focus:ring bg-[var(--card-bg)] text-[var(--foreground)] ${
                                errors.character ? 'border-red-400' : 'border-[var(--border-color)]'
                            }`}
                            placeholder="例如：ELISE_01"
                        />
                        {errors.character && <p className="text-xs text-red-400 mt-1">{errors.character}</p>}
                    </div>

                    <div className="mb-4">
                        <label className="block text-sm font-medium mb-1">職業</label>
                        <select
                            value={job}
                            onChange={(e) => setJob(e.target.value)}
                            className={`w-full border rounded px-3 py-2 focus:outline-none bg-[var(--card-bg)] text-[var(--foreground)] ${
                                errors.job ? 'border-red-400' : 'border-[var(--border-color)]'
                            }`}
                        >
                            {jobs.map((j) => (
                                <option key={j} value={j} className="bg-[var(--card-bg)] text-[var(--foreground)]">
                                    {j}
                                </option>
                            ))}
                        </select>
                        {errors.job && <p className="text-xs text-red-400 mt-1">{errors.job}</p>}
                    </div>

                    <div className="mb-4">
                        <label className="block text-sm font-medium mb-1">攻擊力: {attackPower}</label>
                        <input
                            type="number"
                            value={attackPower}
                            onChange={(e) => setAttackPower(Number(e.target.value))}
                            className={`mt-2 w-full border rounded px-3 py-2 focus:outline-none bg-[var(--card-bg)] text-[var(--foreground)] ${
                                errors.atk ? 'border-red-400' : 'border-[var(--border-color)]'
                            }`}
                        />
                        {errors.atk && <p className="text-xs text-red-400 mt-1">{errors.atk}</p>}
                    </div>

                    <div className="flex items-center justify-between mt-6">
                        <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg shadow">
                            提交
                        </button>
                        <button
                            type="button"
                            onClick={() => {
                                setName('');
                                setId('');
                                setJob('祭司');
                                setAttackPower(50);
                                setEditingCharacter(null);
                                setErrors({});
                            }}
                            className="px-3 py-2 border rounded-lg border-[var(--border-color)]"
                        >
                            重置
                        </button>
                    </div>
                </form>

                {/* 列表 */}
                <div
                    className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 flex flex-col items-center justify-center border border-[var(--border-color)]">
                    <h2 className="text-2xl font-semibold mb-4">角色列表</h2>
                    <table className="w-full text-left border-collapse">
                        <thead>
                        <tr className="border-b">
                            <th className="p-2">名稱</th>
                            <th className="p-2">代碼</th>
                            <th className="p-2">職業</th>
                            <th className="p-2 whitespace-nowrap">攻擊力</th>
                            <th className="p-2">操作</th>
                        </tr>
                        </thead>
                        <tbody>
                        {characters.map(c => (
                            <tr key={c.id} className="border-b hover:bg-[var(--card-bg-hover)]">
                                <td className="p-2 font-semibold">{c.name}</td>
                                <td className="p-2">{c.id}</td>
                                <td className="p-2 whitespace-nowrap">{c.job}</td>
                                <td className="p-2">{c.attackPower}</td>
                                <td className="p-2 flex gap-2 whitespace-nowrap">
                                    <button onClick={() => editCharacters(c)} className="px-2 py-1 bg-yellow-500 text-white rounded">修改</button>
                                    <button onClick={() => deleteCharacters(c.id)} className="px-2 py-1 bg-red-500 text-white rounded">刪除</button>
                                </td>
                            </tr>
                        ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}