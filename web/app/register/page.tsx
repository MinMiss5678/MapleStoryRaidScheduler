"use client"

import React, {useEffect, useState} from "react";
import {useRouter} from "next/navigation";
import {useLoading} from "@/app/providers/LoadingContext";

type Period = {
    id: number;
    startDate: Date;
    endDate: Date;
}

type Character = {
    id: string;
    name: string;
    job: string;
    attackPower: number;
};

type Boss = {
    id: number;
    name: string;
}

type CharacterRegister = {
    id?: number;
    characterId: string | undefined; // 哪個角色
    job: string | null;
    bossId: number | undefined;        // 哪個王團
    rounds: number;
}

interface Register {
    id?: number;
    periodId: number | null;
    weekdays: number[];
    timeslots: string[];      // 時段，例如 "21:00"
    characterRegisters: CharacterRegister[];
    DeleteCharacterRegisterIds: number[];
}

const MAX_ROUNDS = 14;

export default function Register() {
    const { setLoading } = useLoading();
    const [characters, setCharacters] = useState<Character[]>([]);
    const [bosses, setBosses] = useState<Boss[]>([]);
    const [period, setPeriod] = useState<Period>();
    const weekdays = ["日", "一", "二", "三", "四", "五", "六"];
    const timeslots = ["20:00", "21:00", "22:00", "23:00"];
    const [step, setStep] = useState<1 | 2>(1);
    const router = useRouter();

    const [form, setForm] = useState<Register>({
        periodId: null,
        weekdays: [],
        timeslots: [],
        characterRegisters: [],
        DeleteCharacterRegisterIds: []
    });

    useEffect(() => {
            const getPeriod = async () => {
                const res = await fetch("/api/period/GetByNow");
                const data = await res.json();
                setPeriod({
                    id: data.id,
                    startDate: new Date(data.startDate),
                    endDate: new Date(data.endDate),
                });

                updateForm("periodId", data.id);
            }

            const getBosses = async () => {
                const res = await fetch("/api/boss/GetAll");
                const data = await res.json();
                setBosses(data);
            }

            const getCharacter = async () => {
                const res = await fetch("/api/character/GetByDiscordId");
                const data = await res.json();
                setCharacters(data);
            }

            const getRegister = async () => {
                await getPeriod();
                await getBosses();
                await getCharacter();

                const res = await fetch("/api/register");
                if (res.status === 200) {
                    const data = await res.json();
                    setForm({
                        id: data.id,
                        periodId: data.periodId,
                        weekdays: data.weekdays,
                        timeslots: data.timeslots,
                        characterRegisters: data.characterRegisters.map((cr: CharacterRegister) => ({
                            id: cr.id,
                            characterId: cr.characterId,
                            job: cr.job,
                            bossId: cr.bossId,
                            rounds: cr.rounds
                        })),
                        DeleteCharacterRegisterIds: []
                    });
                }
            }

            getRegister();
        }, []
    );

    const updateForm = (key: string, value: string[] | number[]) => {
        setForm(prev => ({...prev, [key]: value}));
    }

    const handleWeekdaysChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const value = Number(e.target.value);
        const checked = e.target.checked;

        const newWeekdays = checked
            ? [...form.weekdays, value]
            : form.weekdays.filter((w) => w !== value);

        updateForm("weekdays", newWeekdays);
    }

    const handleWeekdaysAllCheck = (e: React.ChangeEvent<HTMLInputElement>) => {
        updateForm(
            "weekdays",
            e.target.checked ? weekdays.map((_, i) => i) : []
        )
    }

    const handleTimeslotsChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const value = String(e.target.value);
        const checked = e.target.checked;

        const newTimeslots = checked
            ? [...form.timeslots, value]
            : form.timeslots.filter((w) => w !== value);

        updateForm("timeslots", newTimeslots);
    }

    const handleTimeslotsAllCheck = (e: React.ChangeEvent<HTMLInputElement>) => {
        updateForm(
            "timeslots",
            e.target.checked ? timeslots.map((_) => _) : []
        )
    }

    const addCharacterRegister = () => {
        setForm(prev => ({
            ...prev,
            characterRegisters: [
                ...prev.characterRegisters,
                {characterId: undefined, job: null, bossId: undefined, rounds: 7} // 預設7場
            ]
        }));
    };

    const removeCharacterRegister = (id: number | undefined, index: number) => {
        if (id) {
            setForm(prev => ({
                ...prev,
                DeleteCharacterRegisterIds: [
                    ...(prev.DeleteCharacterRegisterIds ?? []),
                    id
                ]
            }));
        }

        setForm(prev => ({
            ...prev,
            characterRegisters: prev.characterRegisters.filter((_, i) => i !== index)
        }));
    };

    const handleCharacterChange = (index: number, characterId: string) => {
        setForm(prev => {
            const newSignups = [...prev.characterRegisters];
            newSignups[index].characterId = characterId;

            // 可選：更新 job 快照，例如從 characters list 找到職業
            const character = characters.find(c => c.id === characterId);
            newSignups[index].job = character ? character.job : null;

            return {...prev, characterRegisters: newSignups};
        });
    };

    const handleBossChange = (index: number, bossId: number) => {
        setForm(prev => {
            const newSignups = [...prev.characterRegisters];
            newSignups[index].bossId = bossId;
            return {...prev, characterRegisters: newSignups};
        });
    };

    const handleRoundsChange = (index: number, rounds: number) => {
        setForm(prev => {
            const newSignups = [...prev.characterRegisters];
            newSignups[index].rounds = rounds;
            return {...prev, characterRegisters: newSignups};
        });
    };

    const getTotalRounds = (characterId: string | undefined) => {
        return form.characterRegisters
            .filter((s) => s.characterId === characterId)
            .reduce((sum, s) => sum + (s.rounds || 0), 0);
    };

    const getAvailableRounds = (characterId: string | undefined, currentRounds = 0) => {
        const roundOptions = [7, 14];
        if (!characterId) return roundOptions;
        const used = getTotalRounds(characterId) - currentRounds; // 扣掉自己這一列
        const remaining = MAX_ROUNDS - used;
        if (remaining <= 0) return [];  // 已滿
        if (remaining >= 14) return roundOptions;
        if (remaining >= 7) return [7];
        return [remaining];
    };

    const handleNextButton = () => {
        if (form.weekdays.length === 0 || form.timeslots.length === 0)
            return;

        setStep(2);
    }

    const onSubmit = async (e: React.MouseEvent<HTMLButtonElement>) => {
        setLoading(true);
        e.preventDefault();
        
        if (form.id) {
            const res = await fetch("/api/register", {
                method: "PUT",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify(form)
            });

            if (res.ok) {
                alert("修改成功！");
            } else {
                const error = await res.json();
                alert("修改失敗: " + error.message);
            }

            return;
        }

        const res = await fetch("/api/register", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify(form)
        });

        if (res.ok) {
            alert("報名成功！");
            router.push("/");
        } else {
            const error = await res.json();
            alert("報名失敗: " + error.message);
        }
        setLoading(false);
    };

    const cancelRegister = async () => {
        if (form.id !== null) {
            const confirmMessage= "是否取消報名?";
            if (confirmMessage) {
            setLoading(true);
                const res = await fetch(`/api/register/${form.id}`, {method: "DELETE"})
                if (res.ok) {
                    alert("刪除成功")
                    router.push("/");
                } else {
                    const error = await res.json();
                    alert("刪除失敗: " + error.message);
                }
            setLoading(false);
            }
        }
    }
    
    return (
        <div
            className="min-h-screen flex items-start justify-center bg-[var(--background)] text-[var(--foreground)] p-6">
            <div className="w-full max-w-4xl grid grid-cols-1 gap-6">
                <h2>{period?.startDate.toLocaleDateString()} ~ {period?.endDate.toLocaleDateString()}</h2>
                {form.id && <h2>修改報名</h2>}
                {/* 報名表單 */}
                {step === 1 && <div
                    className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 border border-[var(--border-color)] mb-6">
                    <h2 className="text-xl font-semibold mb-4">Step 1：選擇可出團時間</h2>

                    <div className="mb-4">
                        <label className="block font-medium mb-1">可出團星期</label>
                        <div className="flex flex-wrap gap-2">
                            <label className="flex items-center gap-1">
                                <input
                                    type="checkbox"
                                    checked={form.weekdays.length === weekdays.length} // 全部選取時勾選
                                    onChange={handleWeekdaysAllCheck}
                                />
                                全選
                            </label>
                            {weekdays.map((d, i) => (
                                <label key={i} className="flex items-center gap-1">
                                    <input
                                        type="checkbox"
                                        value={i}
                                        checked={form.weekdays.includes(i)}
                                        onChange={handleWeekdaysChange}
                                    />
                                    {d}
                                </label>
                            ))}
                        </div>
                    </div>

                    <div className="mb-4">
                        <label className="block font-medium mb-1">可出團時段</label>
                        <div className="flex flex-wrap gap-2">
                            <label className="flex items-center gap-1">
                                <input
                                    type="checkbox"
                                    checked={form.timeslots.length === timeslots.length} // 全部選取時勾選
                                    onChange={handleTimeslotsAllCheck}
                                />
                                全選
                            </label>
                            {timeslots.map(t => (
                                <label key={t} className="flex items-center gap-1">
                                    <input
                                        type="checkbox"
                                        value={t}
                                        checked={form.timeslots.includes(t)}
                                        onChange={handleTimeslotsChange}
                                    />
                                    {t}
                                </label>
                            ))}
                        </div>
                    </div>
                    <div className="flex gap-2">
                        <button
                            onClick={() => handleNextButton()}
                            className="px-4 py-2 rounded-lg font-semibold shadow-md
                        bg-[var(--btn-blue-bg)] text-white
                        hover:bg-[var(--btn-blue-hover)] 
                        transition-colors duration-200"
                        >
                            下一步
                        </button>
                        {form.id !== null &&
                            <button
                                onClick={() => cancelRegister()}
                                className="px-4 py-2 rounded-lg font-semibold shadow-md bg-red-500 hover:bg-red-600
                            text-white transition-colors duration-200"
                            >
                                取消報名
                            </button>
                        }
                    </div>
                </div>
                }
                {step === 2 &&
                    <div className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 border border-[var(--border-color)]">
                        <h2 className="text-xl font-semibold mb-4">Step 2：角色報名 Boss</h2>
                        {form.characterRegisters.map((s, index) =>
                            <div key={index} className="flex flex-wrap gap-2 items-center mb-2">
                                {/* 選角色 */}
                                <select
                                    value={s.characterId}
                                    onChange={e => handleCharacterChange(index, e.target.value)}
                                    className={"border rounded px-2 py-1 focus:outline-none bg-[var(--card-bg)] text-[var(--foreground)]"}
                                >
                                    <option value="">請選角色</option>
                                    {characters.map(c => {
                                        const isDuplicate = form.characterRegisters.some(
                                            (other, i) => i !== index && other.characterId === c.id && other.bossId === s.bossId
                                        );
                                        const isFull = getTotalRounds(c.id) >= MAX_ROUNDS;
                                        return (
                                            <option key={c.id} value={c.id} disabled={isDuplicate || isFull}>
                                                {c.name}#{c.id}#{c.job} {isFull ? "(已滿)" : ""}
                                            </option>
                                        );
                                    })}
                                </select>

                                {/* 選 Boss */}
                                <select
                                    value={s.bossId}
                                    onChange={e => handleBossChange(index, Number(e.target.value))}
                                    className={"border rounded px-2 py-1 focus:outline-none bg-[var(--card-bg)] text-[var(--foreground)]"}
                                >
                                    <option value={0}>選Boss</option>
                                    {bosses.map(b => {
                                        const isDuplicate = form.characterRegisters.some(
                                            (other, i) => i !== index && other.characterId === s.characterId && other.bossId === b.id
                                        );
                                        return (
                                            <option key={b.id} value={b.id} disabled={isDuplicate}>
                                                {b.name} {isDuplicate ? "(已報名)" : ""}
                                            </option>
                                        );
                                    })}
                                </select>

                                {/* 選場數 */}
                                <select
                                    value={s.rounds || ""}
                                    onChange={(e) => handleRoundsChange(index, Number(e.target.value))}
                                    className={"border rounded px-2 py-1 focus:outline-none bg-[var(--card-bg)] text-[var(--foreground)]"}
                                >
                                    {getAvailableRounds(s.characterId, s.rounds || 0).map(r => (
                                        <option key={r} value={r}>{r}場</option>
                                    ))}
                                </select>

                                {/* 刪除角色 */}
                                <button
                                    type="button"
                                    onClick={() => removeCharacterRegister(s.id, index)}
                                    className="px-3 py-1 bg-red-500 hover:bg-red-600 text-white font-semibold rounded-lg shadow"
                                >
                                    刪除
                                </button>
                            </div>
                        )}
                        <div className="flex gap-2">
                            <button
                                onClick={() => setStep(1)}
                                className="px-4 py-2 rounded-lg font-semibold shadow-md
                                bg-[var(--btn-blue-bg)] text-white
                                hover:bg-[var(--btn-blue-hover)] 
                                transition-colors duration-200"
                            >
                                上一步
                            </button>
                            <button
                                type="button"
                                onClick={addCharacterRegister}
                                className="px-4 py-2 rounded-lg font-semibold shadow-md
                                bg-[var(--btn-blue-bg)] text-white
                                hover:bg-[var(--btn-blue-hover)] 
                                transition-colors duration-200"
                            >
                                新增角色報名
                            </button>

                            <button
                                type="button"
                                onClick={onSubmit}
                                className="px-4 py-2 rounded-lg font-semibold shadow-md
                                bg-[var(--btn-blue-bg)] text-white
                                hover:bg-[var(--btn-blue-hover)] 
                                transition-colors duration-200"
                            >
                                送出報名
                            </button>
                        </div>
                    </div>
                }
            </div>
        </div>
    )
}
