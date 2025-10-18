"use client";

import { useState } from "react";
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

export default function AddCharacterModal({bossId, isOpen, onClose, teamSlot, onAdd}: {
    bossId: number,
    isOpen: boolean,
    onClose: () => void,
    teamSlot: TeamSlot,
    onAdd: (teamSlot: TeamSlot, character: TeamSlotCharacter) => void
}) {
    const [query, setQuery] = useState("");
    const [job, setJob] = useState("");
    const [results, setResults] = useState<TeamSlotCharacter[]>([]);
    const [loading, setLoading] = useState(false);

    const jobs = ['', '祭司', '火毒魔導士', '冰雷魔導士', '狙擊手', '十字軍', '騎士', '龍騎士', '神偷', '暗殺者', '格鬥家', '神槍手'];

    if (!isOpen) return null;

    const handleSearch = async () => {
        setLoading(true);
        const res = await fetch(`/api/register/GetByQuery?bossId=${bossId}&job=${encodeURIComponent(job)}&query=${encodeURIComponent(query)}`);
        const json = await res.json();
        setResults(json);
        setLoading(false);
    };

    const handleAdd = async (character: TeamSlotCharacter) => {
        onAdd(teamSlot, character);
    };

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
            <div className="bg-gray-900 rounded-2xl w-full max-w-lg overflow-hidden shadow-2xl">

                {/* 🟦 標題列 */}
                <div className="flex items-center justify-between bg-blue-700/20 px-6 py-3 border-b border-blue-800/40">
                    <h2 className="text-lg font-semibold text-blue-400">
                        新增成員至 {formatDateTime(teamSlot.slotDateTime)}
                    </h2>
                    <button
                        onClick={onClose}
                        className="text-gray-400 hover:text-white transition"
                    >
                        ✕
                    </button>
                </div>

                {/* 🧩 主體內容 */}
                <div className="p-6">
                    {/* 搜尋區 */}
                    <div className="flex gap-2 mb-4">
                        <select
                            value={job}
                            onChange={(e) => setJob(e.target.value)}
                            className="p-2 rounded bg-gray-800 border border-gray-700 text-gray-200"
                        >
                            {jobs.map((j) => (
                                <option key={j} value={j}>
                                    {j === "" ? "全部職業" : j}
                                </option>
                            ))}
                        </select>
                        <input
                            type="text"
                            value={query}
                            onChange={(e) => setQuery(e.target.value)}
                            placeholder="Discord 名稱 / 角色ID"
                            className="flex-1 p-2 rounded bg-gray-800 border border-gray-700 focus:outline-none focus:ring-1 focus:ring-blue-500"
                        />
                        <button
                            onClick={handleSearch}
                            className="px-4 py-2 bg-blue-600 rounded hover:bg-blue-700 transition"
                        >
                            {loading ? "搜尋中..." : "搜尋"}
                        </button>
                    </div>

                    {/* 🧾 結果區 */}
                    <div className="max-h-60 overflow-y-auto">
                        {/* 表頭 */}
                        {results.length > 0 && (
                            <div className="grid grid-cols-6 text-sm font-semibold text-blue-300 border-b border-gray-600 pb-1 mb-2">
                                <span>Discord 名稱</span>
                                <span>角色名稱</span>
                                <span>職業</span>
                                <span>攻擊力</span>
                                <span>操作</span>
                                <span>場數</span>
                            </div>
                        )}

                        {/* 資料列 */}
                        {results.map((c) => (
                            <div
                                key={c.characterId}
                                className="grid grid-cols-6 items-center border-b text-gray-300  border-gray-700 pb-1 rounded transition"
                            >
                                <span>{c.discordName}</span>
                                <span>{c.characterName}</span>
                                <span>{c.job}</span>
                                <span>{c.attackPower}</span>
                                <span>{c.rounds}</span>
                                <button
                                    onClick={() => handleAdd(c)}
                                    className="px-3 py-1 bg-green-600 rounded hover:bg-green-700 transition text-sm"
                                >
                                    加入
                                </button>
                            </div>
                        ))}

                        {results.length === 0 && !loading && (
                            <div className="text-gray-500 text-center p-4">查無角色</div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
}