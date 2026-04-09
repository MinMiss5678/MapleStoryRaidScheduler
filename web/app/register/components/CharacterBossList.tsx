import React from "react";
import { Boss } from "@/types/raid";
import { Character } from "@/types/character";
import { RegisterFormState, CharacterRegister } from "@/types/register";
import { MAX_ROUNDS } from "@/constants/register";

interface CharacterBossListProps {
    form: RegisterFormState;
    characters: Character[];
    bosses: Boss[];
    onBack: () => void;
    onAdd: () => void;
    onCopyCharacter: (characterId: string) => void;
    onRemove: (id: number | undefined, index: number) => void;
    onCharacterChange: (index: number, characterId: string) => void;
    onBossChange: (index: number, bossId: number) => void;
    onRoundsChange: (index: number, rounds: number) => void;
    onUpsertCharacterRegister: (characterId: string, bossId: number, rounds: number) => void;
    onSubmit: () => void;
    getTotalRounds: (characterId: string | undefined) => number;
    getAvailableRounds: (characterId: string | undefined, currentRounds?: number, currentBossId?: number) => number;
    quickFillBoss: (bossId: number, rounds: number) => void;
}

export const CharacterBossList: React.FC<CharacterBossListProps> = ({
    form,
    characters,
    bosses,
    onBack,
    onAdd,
    onCopyCharacter,
    onRemove,
    onCharacterChange,
    onBossChange,
    onRoundsChange,
    onUpsertCharacterRegister,
    onSubmit,
    getTotalRounds,
    getAvailableRounds,
    quickFillBoss
}) => {
    const [viewMode, setViewMode] = React.useState<"character" | "boss">("character");

    // 按角色分組
    const groupedRegisters: { [key: string]: number[] } = {};
    form.characterRegisters.forEach((reg, index) => {
        const charId = reg.characterId || "unselected";
        if (!groupedRegisters[charId]) {
            groupedRegisters[charId] = [];
        }
        groupedRegisters[charId].push(index);
    });

    const unselectedIndices = groupedRegisters["unselected"] || [];
    const selectedCharIds = Object.keys(groupedRegisters).filter(id => id !== "unselected");

    return (
        <div className="bg-[var(--card-bg)] shadow-md rounded-2xl p-6 border border-[var(--border-color)]">
            <div className="flex justify-between items-center mb-4">
                <h2 className="text-xl font-semibold">Step 2：角色報名 Boss</h2>
                <div className="inline-flex rounded-md shadow-sm" role="group">
                    <button
                        type="button"
                        onClick={() => setViewMode("character")}
                        className={`px-4 py-2 text-sm font-medium border rounded-l-lg transition-colors ${viewMode === "character" ? "bg-blue-500 text-white border-blue-500" : "bg-white text-gray-700 hover:bg-gray-100 dark:bg-gray-800 dark:text-gray-300 dark:border-gray-700 dark:hover:bg-gray-700"}`}
                    >
                        依角色檢視
                    </button>
                    <button
                        type="button"
                        onClick={() => setViewMode("boss")}
                        className={`px-4 py-2 text-sm font-medium border rounded-r-lg transition-colors ${viewMode === "boss" ? "bg-blue-500 text-white border-blue-500" : "bg-white text-gray-700 hover:bg-gray-100 dark:bg-gray-800 dark:text-gray-300 dark:border-gray-700 dark:hover:bg-gray-700"}`}
                    >
                        快速分配 (依 Boss)
                    </button>
                </div>
            </div>

            <div className="space-y-6 mb-6">
                {viewMode === "character" ? (
                    <>
                        {selectedCharIds.map(charId => {
                            const char = characters.find(c => c.id === charId);
                            const totalRounds = getTotalRounds(charId);
                            const remaining = MAX_ROUNDS - totalRounds;

                            return (
                                <div key={charId} className="border border-[var(--border-color)] rounded-xl p-4 bg-gray-50 dark:bg-gray-800/50">
                                    <div className="flex justify-between items-center mb-3 pb-2 border-b border-[var(--border-color)]">
                                        <div className="flex items-center gap-2">
                                            <h3 className="font-bold text-lg text-blue-500">
                                                {char ? `${char.name} (${char.job})` : charId}
                                            </h3>
                                            <button
                                                type="button"
                                                onClick={() => onCopyCharacter(charId)}
                                                disabled={remaining <= 0}
                                                title="為此角色新增另一個 Boss"
                                                className="p-1 rounded bg-blue-100 text-blue-600 hover:bg-blue-200 disabled:opacity-50 disabled:cursor-not-allowed dark:bg-blue-900/30 dark:text-blue-400"
                                            >
                                                <svg xmlns="http://www.w3.org/2000/svg" className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
                                                    <path d="M8 9a1 1 0 011-1h2a1 1 0 110 2H9a1 1 0 01-1-1z" />
                                                    <path fillRule="evenodd" d="M6 3a3 3 0 00-3 3v8a3 3 0 003 3h8a3 3 0 003-3V6a3 3 0 00-3-3H6zM5 6a1 1 0 011-1h8a1 1 0 011 1v8a1 1 0 01-1 1H6a1 1 0 01-1-1V6z" clipRule="evenodd" />
                                                </svg>
                                            </button>
                                        </div>
                                        <div className="flex flex-col items-end">
                                            <span className={`text-sm font-medium ${remaining === 0 ? 'text-red-500' : 'text-green-500'}`}>
                                                剩餘場數: {remaining} / {MAX_ROUNDS}
                                            </span>
                                            {/* 配額進度條 */}
                                            <div className="w-24 h-1.5 bg-gray-200 dark:bg-gray-700 rounded-full mt-1 overflow-hidden">
                                                <div 
                                                    className={`h-full transition-all duration-300 ${remaining === 0 ? 'bg-red-500' : 'bg-green-500'}`}
                                                    style={{ width: `${(totalRounds / MAX_ROUNDS) * 100}%` }}
                                                ></div>
                                            </div>
                                        </div>
                                    </div>
                                    <div className="space-y-2">
                                        {groupedRegisters[charId].map(index => (
                                            <RegisterRow
                                                key={index}
                                                index={index}
                                                register={form.characterRegisters[index]}
                                                characters={characters}
                                                bosses={bosses}
                                                onRemove={onRemove}
                                                onCharacterChange={onCharacterChange}
                                                onBossChange={onBossChange}
                                                onRoundsChange={onRoundsChange}
                                                getAvailableRounds={getAvailableRounds}
                                                form={form}
                                                getTotalRounds={getTotalRounds}
                                                showCharacterSelect={false}
                                            />
                                        ))}
                                    </div>
                                </div>
                            );
                        })}

                        {unselectedIndices.length > 0 && (
                            <div className="border border-dashed border-[var(--border-color)] rounded-xl p-4">
                                <h3 className="font-bold mb-3 text-gray-400 text-sm italic">未選擇角色</h3>
                                <div className="space-y-2">
                                    {unselectedIndices.map(index => (
                                        <RegisterRow
                                            key={index}
                                            index={index}
                                            register={form.characterRegisters[index]}
                                            characters={characters}
                                            bosses={bosses}
                                            onRemove={onRemove}
                                            onCharacterChange={onCharacterChange}
                                            onBossChange={onBossChange}
                                            onRoundsChange={onRoundsChange}
                                            getAvailableRounds={getAvailableRounds}
                                            form={form}
                                            getTotalRounds={getTotalRounds}
                                            showCharacterSelect={true}
                                        />
                                    ))}
                                </div>
                            </div>
                        )}
                    </>
                ) : (
                    <div className="space-y-8">
                        {bosses.map(boss => (
                            <div key={boss.id} className="border border-[var(--border-color)] rounded-xl p-4 bg-gray-50 dark:bg-gray-800/50">
                                <div className="flex justify-between items-center mb-4 pb-2 border-b border-[var(--border-color)]">
                                    <h3 className="font-bold text-lg text-orange-500">{boss.name}</h3>
                                    <div className="flex items-center gap-3">
                                        <span className="text-sm text-gray-500">快速填寫全部角色:</span>
                                        <div className="flex gap-1">
                                            {[0, 7, 14].map(r => (
                                                <button
                                                    key={r}
                                                    type="button"
                                                    onClick={() => quickFillBoss(boss.id, r)}
                                                    className="px-3 py-1 text-xs bg-white border border-gray-300 rounded hover:bg-gray-100 dark:bg-gray-700 dark:border-gray-600 dark:hover:bg-gray-600"
                                                >
                                                    {r === 0 ? "清除" : `${r}場`}
                                                </button>
                                            ))}
                                        </div>
                                    </div>
                                </div>
                                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                    {characters.map(char => {
                                        const regIndex = form.characterRegisters.findIndex(r => r.characterId === char.id && r.bossId === boss.id);
                                        const register = regIndex !== -1 ? form.characterRegisters[regIndex] : { characterId: char.id, bossId: boss.id, rounds: 0 };
                                        const currentRounds = register.rounds || 0;
                                        const maxAvailable = getAvailableRounds(char.id, currentRounds, boss.id);
                                        const totalRounds = getTotalRounds(char.id);
                                        const remaining = MAX_ROUNDS - totalRounds;

                                        return (
                                            <div key={char.id} className="flex items-center justify-between p-2 bg-white dark:bg-gray-900 rounded-lg border border-[var(--border-color)]">
                                                <div className="flex flex-col">
                                                    <span className="font-medium text-sm">{char.name}</span>
                                                    <span className="text-[10px] text-gray-400">{char.job} (剩餘 {remaining})</span>
                                                </div>
                                                <div className="flex items-center gap-1">
                                                    <button
                                                        type="button"
                                                        onClick={() => {
                                                            if (regIndex !== -1) {
                                                                onRoundsChange(regIndex, Math.max(0, currentRounds - 1));
                                                            }
                                                        }}
                                                        disabled={currentRounds === 0}
                                                        className="w-6 h-6 flex items-center justify-center border border-border rounded bg-gray-50 hover:bg-gray-100 dark:bg-gray-800 dark:hover:bg-gray-700 disabled:opacity-30"
                                                    > - </button>
                                                    <input
                                                        type="number"
                                                        value={currentRounds || ""}
                                                        onChange={(e) => {
                                                            const val = Math.max(0, parseInt(e.target.value) || 0);
                                                            const finalVal = Math.min(val, maxAvailable);
                                                            onUpsertCharacterRegister(char.id, boss.id, finalVal);
                                                        }}
                                                        className="w-10 text-center border border-border rounded text-sm p-1 bg-background text-foreground dark:bg-zinc-900 focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all"
                                                    />
                                                    <button
                                                        type="button"
                                                        onClick={() => {
                                                            onUpsertCharacterRegister(char.id, boss.id, Math.min(maxAvailable, currentRounds + 1));
                                                        }}
                                                        className="w-6 h-6 flex items-center justify-center border border-border rounded bg-gray-50 hover:bg-gray-100 dark:bg-gray-800 dark:hover:bg-gray-700"
                                                    > + </button>
                                                </div>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>

            <div className="flex flex-wrap gap-2">
                <button
                    onClick={onBack}
                    className="px-4 py-2 rounded-lg font-semibold shadow-md bg-gray-500 hover:bg-gray-600 dark:bg-gray-600 dark:hover:bg-gray-700 text-white transition-colors duration-200"
                >
                    上一步
                </button>
                <button
                    type="button"
                    onClick={onAdd}
                    className="px-4 py-2 rounded-lg font-semibold shadow-md bg-[var(--btn-blue-bg)] text-white hover:bg-[var(--btn-blue-hover)] transition-colors duration-200"
                >
                    新增報名項目
                </button>
                <button
                    type="button"
                    onClick={onSubmit}
                    className="px-4 py-2 rounded-lg font-semibold shadow-md bg-green-600 hover:bg-green-700 dark:bg-green-700 dark:hover:bg-green-800 text-white transition-colors duration-200"
                >
                    送出報名
                </button>
            </div>
        </div>
    );
};

interface RegisterRowProps {
    index: number;
    register: CharacterRegister;
    characters: Character[];
    bosses: Boss[];
    onRemove: (id: number | undefined, index: number) => void;
    onCharacterChange: (index: number, characterId: string) => void;
    onBossChange: (index: number, bossId: number) => void;
    onRoundsChange: (index: number, rounds: number) => void;
    getAvailableRounds: (characterId: string | undefined, currentRounds?: number, currentBossId?: number) => number;
    form: RegisterFormState;
    getTotalRounds: (characterId: string | undefined) => number;
    showCharacterSelect: boolean;
}

const RegisterRow: React.FC<RegisterRowProps> = ({
    index,
    register,
    characters,
    bosses,
    onRemove,
    onCharacterChange,
    onBossChange,
    onRoundsChange,
    getAvailableRounds,
    form,
    getTotalRounds,
    showCharacterSelect
}) => {
    return (
        <div className="flex flex-wrap gap-2 items-center bg-white dark:bg-gray-900 p-2 rounded-lg shadow-sm border border-[var(--border-color)]">
            {showCharacterSelect && (
                <select
                    value={register.characterId || ""}
                    onChange={e => onCharacterChange(index, e.target.value)}
                    className="border border-border rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-blue-500 bg-background text-foreground min-w-[150px] dark:bg-zinc-900 transition-all"
                >
                    <option value="" className="bg-white dark:bg-zinc-900">請選角色</option>
                    {characters.map(c => {
                        const isFull = getTotalRounds(c.id) >= MAX_ROUNDS;
                        return (
                            <option key={c.id} value={c.id} disabled={isFull} className="bg-white dark:bg-zinc-900">
                                {c.name}#{c.job} {isFull ? "(已滿)" : ""}
                            </option>
                        );
                    })}
                </select>
            )}

            {/* 選 Boss */}
            <select
                value={register.bossId || 0}
                onChange={e => onBossChange(index, Number(e.target.value))}
                className="border border-border rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-blue-500 bg-background text-foreground min-w-[120px] dark:bg-zinc-900 transition-all"
            >
                <option value={0} className="bg-white dark:bg-zinc-900">選Boss</option>
                {bosses.map(b => {
                    const isDuplicate = form.characterRegisters.some(
                        (other, i) => i !== index && other.characterId === register.characterId && other.bossId === b.id
                    );
                    return (
                        <option key={b.id} value={b.id} disabled={isDuplicate} className="bg-white dark:bg-zinc-900">
                            {b.name} {isDuplicate ? "(已報名)" : ""}
                        </option>
                    );
                })}
            </select>

            {/* 選場數 */}
            <div className="flex items-center gap-1">
                <button
                    type="button"
                    onClick={() => onRoundsChange(index, Math.max(0, (register.rounds || 0) - 1))}
                    className="w-8 h-8 flex items-center justify-center border rounded bg-gray-50 hover:bg-gray-100 dark:bg-gray-800 dark:hover:bg-gray-700"
                >
                    -
                </button>
                <input
                    type="number"
                    min={0}
                    max={getAvailableRounds(register.characterId, register.rounds || 0, register.bossId)}
                    value={register.rounds || ""}
                    onChange={(e) => {
                        const val = Math.max(0, parseInt(e.target.value) || 0);
                        const max = getAvailableRounds(register.characterId, register.rounds || 0, register.bossId);
                        onRoundsChange(index, Math.min(val, max));
                    }}
                    placeholder="場數"
                    className="border border-border rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-blue-500 bg-background text-foreground w-12 text-center dark:bg-zinc-900 transition-all"
                />
                <button
                    type="button"
                    onClick={() => {
                        const max = getAvailableRounds(register.characterId, register.rounds || 0, register.bossId);
                        onRoundsChange(index, Math.min(max, (register.rounds || 0) + 1));
                    }}
                    className="w-8 h-8 flex items-center justify-center border rounded bg-gray-50 hover:bg-gray-100 dark:bg-gray-800 dark:hover:bg-gray-700"
                >
                    +
                </button>
                
                {/* 快速按鈕 */}
                <div className="flex gap-1 ml-2">
                    {[7, 14].map(r => {
                        const max = getAvailableRounds(register.characterId, register.rounds || 0, register.bossId);
                        const isDisabled = r > max;
                        return (
                            <button
                                key={r}
                                type="button"
                                onClick={() => onRoundsChange(index, r)}
                                disabled={isDisabled}
                                className="px-2 py-1 text-xs bg-blue-50 text-blue-600 border border-blue-200 rounded hover:bg-blue-100 disabled:opacity-50 disabled:cursor-not-allowed dark:bg-blue-900/20 dark:text-blue-400 dark:border-blue-800"
                            >
                                {r}
                            </button>
                        );
                    })}
                    <button
                        type="button"
                        onClick={() => {
                            const max = getAvailableRounds(register.characterId, register.rounds || 0, register.bossId);
                            onRoundsChange(index, max);
                        }}
                        className="px-2 py-1 text-xs bg-orange-50 text-orange-600 border border-orange-200 rounded hover:bg-orange-100 dark:bg-orange-900/20 dark:text-orange-400 dark:border-orange-800"
                    >
                        Max
                    </button>
                </div>

                <span className="ml-1 text-sm text-gray-500 whitespace-nowrap">
                    場
                    {(() => {
                        const boss = register.bossId ? bosses.find(b => b.id === register.bossId) : undefined;
                        if (boss && boss.roundConsumption > 1) {
                            return (
                                <span className="ml-1 text-sm text-orange-500 font-medium">
                                    (每場消耗 {boss.roundConsumption} 次)
                                </span>
                            );
                        }
                        return null;
                    })()}
                </span>
            </div>

            {/* 刪除按鈕 */}
            <button
                type="button"
                onClick={() => onRemove(register.id, index)}
                className="ml-auto px-2 py-1 text-red-500 hover:text-white hover:bg-red-500 dark:text-red-400 dark:hover:bg-red-600 dark:hover:text-white rounded transition-colors"
            >
                <svg xmlns="http://www.w3.org/2000/svg" className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
                </svg>
            </button>
        </div>
    );
};
