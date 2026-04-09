"use client";

import { useState, useEffect, useCallback } from "react";
import {formatDateTime} from "@/utils/dateTimeUtil";
import {TeamSlot, TeamSlotCharacter} from "@/types/raid";
import {Search, UserPlus, CheckCircle2} from "lucide-react";
import { jobCategoryService } from "@/services/bossService";
import { registerService } from "@/services/registerService";
import { Modal } from "@/components/ui/Modal";
import { Button, Input, Select } from "@/components/ui/FormControls";

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
    const [jobs, setJobs] = useState<string[]>([]);

    useEffect(() => {
        const fetchJobs = async () => {
            if (!isOpen || jobs.length > 0) return;
            try {
                const jobMap = await jobCategoryService.getJobMap();
                setJobs(Object.keys(jobMap));
            } catch (error) {
                console.error("Failed to fetch jobs:", error);
            }
        };
        fetchJobs();
    }, [isOpen, jobs.length]);

    const handleSearch = useCallback(async () => {
        if (!isOpen) return;
        setLoading(true);
        try {
            const params = new URLSearchParams({
                bossId: String(bossId),
                job: job ?? "",
                query: query ?? "",
                slotDateTime: teamSlot.slotDateTime instanceof Date 
                    ? teamSlot.slotDateTime.toISOString() 
                    : String(teamSlot.slotDateTime)
            });

            const data = await registerService.getByQuery(params.toString());
            setResults(data ?? []);
        } catch (error) {
            console.error("Search failed:", error);
        } finally {
            setLoading(false);
        }
    }, [bossId, job, query, isOpen, teamSlot.slotDateTime]);

    // Debounce search
    useEffect(() => {
        const timer = setTimeout(() => {
            if (query || job) {
                handleSearch();
            }
        }, 500);
        return () => clearTimeout(timer);
    }, [query, job, handleSearch]);

    if (!isOpen) return null;

    const isAlreadyInTeam = (characterId: string | null) => {
        if (!characterId) return false;
        return teamSlot.characters.some(c => c.characterId === characterId);
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="2xl"
            title={
                <div className="space-y-1">
                    <h2 className="text-xl font-bold">新增成員</h2>
                    <p className="text-xs text-muted-foreground">時段: {formatDateTime(teamSlot.slotDateTime)}</p>
                </div>
            }
            footer={
                <Button variant="outline" onClick={onClose}>
                    關閉
                </Button>
            }
        >
            {/* Search Controls */}
            <div className="flex flex-col md:flex-row gap-3 mb-6">
                <Input
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    placeholder="Discord 名稱 / 角色ID"
                    leftIcon={<Search className="text-muted-foreground" size={18} />}
                    containerClassName="flex-1"
                />
                <Select
                    value={job}
                    onChange={(e) => setJob(e.target.value)}
                    className="min-w-[140px]"
                >
                    <option value="">全部職業</option>
                    {jobs.map((j) => (
                        <option key={j} value={j}>
                            {j}
                        </option>
                    ))}
                </Select>
                <Button
                    onClick={handleSearch}
                    isLoading={loading}
                    leftIcon={!loading && <Search size={18} />}
                >
                    搜尋
                </Button>
            </div>

            {/* Results Table */}
            <div className="border border-border rounded-xl overflow-hidden bg-background">
                <div className="max-h-[400px] overflow-y-auto">
                    <table className="w-full text-sm">
                        <thead className="bg-muted/30 sticky top-0 z-10">
                            <tr className="text-muted-foreground border-b border-border">
                                <th className="text-left font-medium p-3">玩家 / 角色</th>
                                <th className="text-left font-medium p-3">職業</th>
                                <th className="text-right font-medium p-3">攻擊力</th>
                                <th className="text-center font-medium p-3">場數</th>
                                <th className="text-right font-medium p-3 w-24">操作</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-border/50">
                            {results.map((c) => {
                                const added = isAlreadyInTeam(c.characterId);
                                return (
                                    <tr key={c.characterId} className="hover:bg-muted/20 transition-colors">
                                        <td className="p-3">
                                            <div className="flex flex-col">
                                                <span className="font-semibold">{c.characterName}</span>
                                                <span className="text-xs text-muted-foreground">{c.discordName}</span>
                                            </div>
                                        </td>
                                        <td className="p-3">
                                            <span className="px-2 py-0.5 bg-muted rounded text-xs">{c.job}</span>
                                        </td>
                                        <td className="p-3 text-right font-mono">{c.attackPower.toLocaleString()}</td>
                                        <td className="p-3 text-center">{c.rounds}</td>
                                        <td className="p-3 text-right">
                                            {added ? (
                                                <span className="flex items-center justify-end gap-1 text-green-600 dark:text-green-400 font-medium">
                                                    <CheckCircle2 size={16} />
                                                    已加入
                                                </span>
                                            ) : (
                                                <Button
                                                    size="sm"
                                                    variant="outline"
                                                    onClick={() => onAdd(teamSlot, c)}
                                                    className="ml-auto text-blue-600 dark:text-blue-400 border-blue-100 dark:border-blue-900/30 hover:bg-blue-600 hover:text-white"
                                                    leftIcon={<UserPlus size={14} />}
                                                >
                                                    加入
                                                </Button>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                            {results.length === 0 && !loading && (
                                <tr>
                                    <td colSpan={5} className="p-12 text-center text-muted-foreground italic">
                                        {query || job ? "查無角色" : "請輸入關鍵字搜尋角色"}
                                    </td>
                                </tr>
                            )}
                            {loading && results.length === 0 && (
                                <tr>
                                    <td colSpan={5} className="p-12 text-center">
                                        <div className="flex flex-col items-center justify-center gap-2">
                                            <Search className="animate-pulse text-blue-500" size={32} />
                                            <p className="text-muted-foreground">搜尋中...</p>
                                        </div>
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>
            </div>
        </Modal>
    );
}