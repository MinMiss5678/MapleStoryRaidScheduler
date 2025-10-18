"use client";

import {useState, useEffect} from "react";
import RaidTeamCard from "./components/RaidTeamCard";
import {useLoading} from "@/app/providers/LoadingContext";

type TeamSlotCharacter = {
    characterId: string;
    discordId: number;
    discordName: string;
    characterName: string;
    job: string;
    attackPower: number;
    rounds: number;
}

type TeamSlot = {
    id: number;
    bossId: number;
    slotDateTime: Date;
    characters: TeamSlotCharacter[];
    deleteCharacterIds: string[];
    isTemporary?: boolean;
}

type Boss = {
    id: number;
    name: string;
    requireMembers: number;
};

export default function RaidSchedulerPage() {
    const [bosses, setBosses] = useState<Boss[]>([]);
    const [selectedBoss, setSelectedBoss] = useState<Boss>();
    const [minMembers, setMinMembers] = useState<number>(0);
    const [teamSlots, setTeamSlots] = useState<TeamSlot[]>([]);
    const [deleteTeamSlotIds, setDeleteTeamSlotIds] = useState<number[]>([]);
    const { setLoading } = useLoading();
    const [manualSlotDateTime, setManualSlotDateTime] = useState<string>(() => {
        const now = new Date();
        now.setMinutes(0, 0, 0); // 強制整點
        return now.toISOString().slice(0, 16); // YYYY-MM-DDTHH:MM
    });

    useEffect(() => {
        async function loadBosses() {
            const res = await fetch("/api/boss/GetAll");
            const data = await res.json();
            setBosses(data);
            if (data.length > 0) {
                setSelectedBoss(data[0]);
                setMinMembers(data[0].requireMembers);
            }
        }

        loadBosses();
    }, []);

    useEffect(() => {
        async function loadTeamSlots() {
            if (!selectedBoss) return;
            const res = await fetch(`/api/teamSlot?bossId=${selectedBoss.id}`);
            if (res.ok) {
                const data = await res.json();
                setTeamSlots(data);
            }
        }

        loadTeamSlots();
    }, [selectedBoss]);

    const handleAutoSchedule = async () => {
        const res = await fetch("/api/schedule/AutoSchedule", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({
                bossId: selectedBoss?.id,
                minMembers: minMembers
            }),
        });
        if (res.ok) {
            const data = await res.json();
            setTeamSlots(data);
        } else {
            alert("自動排團失敗");
        }
    };

    const handleAddManualTeamSlot = () => {
        if (!manualSlotDateTime || !selectedBoss) return;

        const [datePart, timePart] = manualSlotDateTime.split("T");
        const [hour] = timePart.split(":").map(Number);

        // 建立 Date 並強制整點
        const slotDate = new Date(datePart);
        slotDate.setHours(hour, 0, 0, 0);

        const newTeamSlot: TeamSlot = {
            id: Math.floor(Math.random() * 1_000_000_000),
            bossId: selectedBoss.id,
            slotDateTime: slotDate,
            characters: [],
            deleteCharacterIds: [],
            isTemporary: true,
        };

        setTeamSlots(prev => [...prev, newTeamSlot]);
        setManualSlotDateTime(""); // 清空輸入
    };

    const handleConfirmSchedule = async () => {
        if (teamSlots.length === 0 && deleteTeamSlotIds.length === 0) return;
        setLoading(true);

        const res = await fetch("/api/teamSlot", {
            method: "PUT",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({
                bossId: selectedBoss?.id,
                teamSlots: teamSlots,
                deleteTeamSlotIds: deleteTeamSlotIds
            })
        });
        
        if (res.ok) {
            const data = await res.json();
            alert("排團已儲存！")
            setDeleteTeamSlotIds([]);
            setTeamSlots(data);
        }
        else alert("儲存失敗");
        setLoading(false);
    };

    const onTeamSlotUpdate = (updatedTeamSlot: TeamSlot) => {
        setTeamSlots(prev => prev.map(t => t.id === updatedTeamSlot.id ? updatedTeamSlot : t));
    };

    const onTeamSlotDelete = (teamSlot: TeamSlot) => {
        if (!teamSlot.isTemporary) {
            setDeleteTeamSlotIds(deleteTeamSlotIds.concat(teamSlot.id));
        }
        setTeamSlots((prev) => prev.filter((t) => t.id !== teamSlot.id));
    }

    const sameDay = (d1: Date, d2: Date) => {
        const slotDate = new Date(d1);
        const teamDate = new Date(d2);

        return (
            slotDate.getUTCFullYear() === teamDate.getUTCFullYear() &&
            slotDate.getUTCMonth() === teamDate.getUTCMonth() &&
            slotDate.getUTCDate() === teamDate.getUTCDate())
    }

    const onAddCharacter = (teamSlot: TeamSlot, character: TeamSlotCharacter) => {
        const errors: string[] = [];
        
        const alreadyPlayedToday = teamSlots
            .filter(slot => slot.id !== teamSlot.id)
            .some(slot =>
                slot.characters.some(c => c.discordId === character.discordId) &&
                sameDay(slot.slotDateTime, teamSlot.slotDateTime)
        );

        if (alreadyPlayedToday) {
            errors.push("該玩家今天已經加入其他隊伍，不能再加入！");
        }

        const totalRounds = teamSlots
            .flatMap(slot => slot.characters)
            .filter(c => c.characterId === character.characterId)
            .reduce((sum) => sum + 7, 0);

        if (totalRounds + 7 > character.rounds) {
            errors.push("角色場數已達上限！");
        }

        if (teamSlot.characters.length === selectedBoss?.requireMembers) {
            errors.push("隊伍已滿");
        }
        
        if (teamSlot.characters.find(x => x.discordId === character.discordId)) {
            errors.push("玩家重複");
        }
        
        if (teamSlot.characters.find(x => x.characterId === character.characterId)) {
            errors.push("角色重複");
        }
        
        if (errors.length > 0) {
            alert(errors.join("\r\n"))
            return;
        }

        const updatedTeam = {
            ...teamSlot,
            characters: [...teamSlot.characters, character],
        };
        onTeamSlotUpdate(updatedTeam);
    };

    return (
        <div className="min-h-screen bg-gray-950 text-gray-100 p-8">
            {/* 上方：自動排團 + 手動新增隊伍，水平排列 */}
            <div className="flex gap-6 mb-8">
                {/* 左側：自動排團區塊 */}
                <div className="w-120 bg-gray-800 p-6 rounded-2xl shadow">
                    <h2 className="text-xl mb-3 font-semibold">🧠 自動排團</h2>
                    <p className="text-gray-400 mb-4">
                        選擇 Boss 並設定最少隊伍人數，自動產生最適組合。
                    </p>

                    {/* Boss 選單 */}
                    <div className="flex items-center gap-2 mb-4">
                        <label className="text-gray-300">選擇 Boss：</label>
                        <select
                            value={selectedBoss?.id || ""}
                            onChange={(e) => {
                                const boss = bosses.find(b => b.id === Number(e.target.value));
                                setSelectedBoss(boss);
                                if (boss) setMinMembers(boss.requireMembers);
                            }}
                            className="p-1 rounded bg-gray-800 border border-gray-700"
                        >
                            {bosses.map(b => (
                                <option key={b.id} value={b.id}>{b.name}</option>
                            ))}
                        </select>
                    </div>

                    {/* 最少隊伍人數 */}
                    <div className="flex items-center gap-2 mb-4">
                        <label className="text-gray-300">最少隊伍人數：</label>
                        <input
                            type="number"
                            min={2}
                            value={minMembers}
                            onChange={(e) => setMinMembers(Number(e.target.value))}
                            className="w-16 p-1 rounded bg-gray-800 border border-gray-700 text-center"
                        />
                    </div>

                    {/* 開始自動排團按鈕 */}
                    <div className="flex gap-2 mb-4">
                        <button
                            onClick={handleAutoSchedule}
                            className="px-6 py-3 bg-blue-600 rounded-lg hover:bg-blue-700 disabled:bg-gray-600"
                        >
                            開始自動排團
                        </button>
                    </div>
                </div>

                {/* 右側：手動新增隊伍區塊 */}
                <div className="w-80 bg-gray-800 p-6 rounded-2xl shadow">
                    <h2 className="text-xl mb-3 font-semibold">➕ 手動新增隊伍</h2>
                    <div className="flex items-center gap-2 mb-4">
                        <label className="text-gray-300">選擇日期時間：</label>
                        <input
                            type="datetime-local"
                            value={manualSlotDateTime}
                            onChange={(e) => setManualSlotDateTime(e.target.value)}
                            className="p-1 rounded bg-gray-800 border border-gray-700"
                            step={3600} // 每小時整點
                        />
                    </div>
                    <button
                        onClick={handleAddManualTeamSlot}
                        className="px-4 py-2 bg-purple-600 rounded hover:bg-purple-700"
                    >
                        新增隊伍
                    </button>
                </div>
            </div>

            {/* 隊伍列表 */}
            {selectedBoss && (
                <div className="flex flex-wrap gap-4 mb-8">
                    {teamSlots.map((teamSlot, i) => (
                        <RaidTeamCard
                            key={i}
                            bossId={selectedBoss.id}
                            teamSlot={teamSlot}
                            onTeamSlotUpdate={onTeamSlotUpdate}
                            onTeamSlotDelete={onTeamSlotDelete}
                            onAddCharacter={onAddCharacter}
                        />
                    ))}
                </div>
            )}

            {/* 儲存按鈕 */}
            <div className="flex justify-end">
                <button
                    onClick={handleConfirmSchedule}
                    className="px-6 py-3 bg-green-600 rounded-lg hover:bg-green-700"
                >
                    儲存
                </button>
            </div>
        </div>
    );
}