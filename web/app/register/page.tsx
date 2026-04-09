"use client"

import React, { useState } from "react";
import { useRegisterForm } from "@/hooks/useRegisterForm";
import { TimePicker } from "./components/TimePicker";
import { CharacterBossList } from "./components/CharacterBossList";

export default function Register() {
    const {
        period,
        characters,
        bosses,
        form,
        isRegisterLoading,
        quickFill,
        copyDay,
        toggleAvailability,
        handleWeekdayAllCheck,
        handleTimeslotAllCheck,
        addCharacterRegister,
        copyCharacterRegister,
        removeCharacterRegister,
        handleCharacterChange,
        handleBossChange,
        handleRoundsChange,
        upsertCharacterRegister,
        getTotalRounds,
        getAvailableRounds,
        quickFillBoss,
        onSubmit,
        cancelRegister,
    } = useRegisterForm();

    const [step, setStep] = useState<1 | 2>(1);

    if (isRegisterLoading) {
        return <div className="min-h-screen flex items-center justify-center">載入中...</div>;
    }

    return (
        <div className="min-h-screen flex items-start justify-center bg-[var(--background)] text-[var(--foreground)] p-6">
            <div className="w-full max-w-4xl grid grid-cols-1 gap-6">
                {period && (
                    <div className="flex justify-between items-end border-b border-[var(--border-color)] pb-2 mb-2">
                        <div>
                            <h1 className="text-2xl font-bold">Boss 報名系統</h1>
                            <p className="text-gray-500">
                                週期: {period.startDate.toLocaleDateString()} ~ {period.endDate.toLocaleDateString()}
                            </p>
                        </div>
                        {form.id && (
                            <span className="bg-yellow-100 text-yellow-800 text-xs font-medium px-2.5 py-0.5 rounded dark:bg-yellow-900 dark:text-yellow-300">
                                修改現有報名
                            </span>
                        )}
                    </div>
                )}

                {step === 1 && (
                    <TimePicker
                        form={form}
                        quickFill={quickFill}
                        copyDay={copyDay}
                        toggleAvailability={toggleAvailability}
                        handleWeekdayAllCheck={handleWeekdayAllCheck}
                        handleTimeslotAllCheck={handleTimeslotAllCheck}
                        onNext={() => setStep(2)}
                        onCancel={cancelRegister}
                        hasId={!!form.id}
                    />
                )}

                {step === 2 && (
                    <CharacterBossList
                        form={form}
                        characters={characters}
                        bosses={bosses}
                        onBack={() => setStep(1)}
                        onAdd={addCharacterRegister}
                        onCopyCharacter={copyCharacterRegister}
                        onRemove={removeCharacterRegister}
                        onCharacterChange={handleCharacterChange}
                        onBossChange={handleBossChange}
                        onRoundsChange={handleRoundsChange}
                        onUpsertCharacterRegister={upsertCharacterRegister}
                        onSubmit={onSubmit}
                        getTotalRounds={getTotalRounds}
                        getAvailableRounds={getAvailableRounds}
                        quickFillBoss={quickFillBoss}
                    />
                )}
            </div>
        </div>
    );
}
