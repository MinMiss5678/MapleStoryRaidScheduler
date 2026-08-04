"use client";

import { useRole } from "@/app/providers/RolesProvider";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { scheduleService } from "@/services/scheduleService";
import { systemConfigService } from "@/services/systemConfigService";
import { registerService } from "@/services/registerService";
import { SystemConfig } from "@/types/system";
import { TeamSlot } from "@/types/raid";
import { useCharacters } from "@/hooks/queries/useCharacters";
import {
    CalendarDays, Clock, Users, UserCircle,
    Swords, LogIn, ChevronRight, Shield
} from "lucide-react";

const DAYS = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"];

function formatCountdown(ms: number): string {
    if (ms <= 0) return "已截止";
    const totalSecs = Math.floor(ms / 1000);
    const days = Math.floor(totalSecs / 86400);
    const hours = Math.floor((totalSecs % 86400) / 3600);
    const mins = Math.floor((totalSecs % 3600) / 60);
    if (days > 0) return `${days} 天 ${hours} 小時`;
    if (hours > 0) return `${hours} 小時 ${mins} 分`;
    return `${mins} 分鐘`;
}

// ─── 未登入 Landing ───────────────────────────────────────────────
function LandingView() {
    const router = useRouter();
    return (
        <div className="min-h-[calc(100vh-4rem)] flex flex-col items-center justify-center p-8 text-center">
            <div className="max-w-lg">
                <div className="flex justify-center mb-6">
                    <div className="p-4 bg-blue-100 dark:bg-blue-900/30 rounded-2xl">
                        <Swords className="w-12 h-12 text-blue-600 dark:text-blue-400" />
                    </div>
                </div>
                <h1 className="text-4xl font-bold tracking-tight mb-3">
                    MapleStory<br />Raid Scheduler
                </h1>
                <p className="text-muted-foreground text-lg mb-8">
                    透過 Discord 登入，快速完成 Boss 報名與查看排團結果。
                </p>
                <button
                    onClick={() => window.location.href = "/api/auth/discord"}
                    className="inline-flex items-center gap-3 px-8 py-3 bg-[#5865F2] hover:bg-[#4752C4] text-white rounded-xl font-semibold text-lg transition-all shadow-lg shadow-indigo-500/20"
                >
                    <LogIn className="w-5 h-5" />
                    用 Discord 登入
                </button>
            </div>
        </div>
    );
}

// ─── 已登入 Dashboard ─────────────────────────────────────────────
function DashboardView() {
    const { role } = useRole();
    const { data: myCharacters = [] } = useCharacters();
    const [allTeams, setAllTeams] = useState<TeamSlot[]>([]);
    const [config, setConfig] = useState<SystemConfig | null>(null);
    const [deadline, setDeadline] = useState<string | null>(null);
    const [countdown, setCountdown] = useState<string>("");
    const [loading, setLoading] = useState(true);

    // 與 scheduleResult 邏輯一致：只計算仍有效角色的隊伍
    const myTeamCount = allTeams.filter(team =>
        team.characters.some(c => myCharacters.some(mc => mc.id === c.characterId))
    ).length;

    useEffect(() => {
        async function load() {
            try {
                const [teams, cfg, dl] = await Promise.all([
                    scheduleService.getByDiscordId(),
                    systemConfigService.getConfig(),
                    registerService.getDeadline(),
                ]);
                setAllTeams(teams);
                setConfig(cfg);
                setDeadline(dl);
            } finally {
                setLoading(false);
            }
        }
        load();
    }, []);

    // 每分鐘更新倒數。截止時間用後端算好的 deadline（權威、週期相對），前端不自行重算。
    useEffect(() => {
        const tick = () => {
            if (!deadline) { setCountdown("—"); return; }  // 沒有 active period → 無截止可顯示
            setCountdown(formatCountdown(new Date(deadline).getTime() - Date.now()));
        };
        tick();
        const id = setInterval(tick, 60_000);
        return () => clearInterval(id);
    }, [deadline]);

    const userQuickLinks = [
        { label: "排團結果", desc: "查看本週已排定的隊伍", href: "/scheduleResult", icon: CalendarDays, color: "blue" },
        { label: "補位系統", desc: "為公開隊伍填補空缺", href: "/schedule", icon: Users, color: "green" },
        { label: "Boss 報名", desc: "報名本週想打的 Boss", href: "/register", icon: Swords, color: "purple" },
        { label: "角色管理", desc: "新增或管理你的角色", href: "/character", icon: UserCircle, color: "orange" },
    ];

    const adminQuickLinks = [
        { label: "排團管理", desc: "管理本週排團與隊伍分配", href: "/admin/schedule", icon: Shield, color: "red" },
        ...userQuickLinks,
    ];

    const quickLinks = role === "admin" ? adminQuickLinks : userQuickLinks;

    const colorMap: Record<string, string> = {
        blue:   "bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400",
        green:  "bg-green-100 dark:bg-green-900/30 text-green-600 dark:text-green-400",
        purple: "bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400",
        orange: "bg-orange-100 dark:bg-orange-900/30 text-orange-600 dark:text-orange-400",
        red:    "bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400",
    };

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground">
            <div className="max-w-4xl mx-auto">
                <h1 className="text-3xl font-bold tracking-tight mb-8">本週總覽</h1>

                {/* 統計卡 */}
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-8">
                    {/* 我的場次 */}
                    <div className="bg-card border border-border rounded-2xl p-6 flex items-center gap-4 shadow-sm">
                        <div className="p-3 bg-blue-100 dark:bg-blue-900/30 rounded-xl">
                            <CalendarDays className="w-6 h-6 text-blue-600 dark:text-blue-400" />
                        </div>
                        <div>
                            <p className="text-sm text-muted-foreground">本週已排場次</p>
                            {loading ? (
                                <div className="h-8 w-12 bg-muted animate-pulse rounded mt-1" />
                            ) : (
                                <p className="text-3xl font-bold">{myTeamCount}</p>
                            )}
                        </div>
                    </div>

                    {/* 報名截止倒數 */}
                    <div className="bg-card border border-border rounded-2xl p-6 flex items-center gap-4 shadow-sm">
                        <div className="p-3 bg-amber-100 dark:bg-amber-900/30 rounded-xl">
                            <Clock className="w-6 h-6 text-amber-600 dark:text-amber-400" />
                        </div>
                        <div>
                            <p className="text-sm text-muted-foreground">
                                報名截止（
                                {config ? `${DAYS[config.deadlineDayOfWeek]} ${config.deadlineTime}` : "—"}
                                ）
                            </p>
                            {loading ? (
                                <div className="h-8 w-28 bg-muted animate-pulse rounded mt-1" />
                            ) : (
                                <p className="text-3xl font-bold">{countdown}</p>
                            )}
                        </div>
                    </div>
                </div>

                {/* 快速入口 */}
                <h2 className="text-lg font-semibold mb-4 text-muted-foreground">快速入口</h2>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    {quickLinks.map(({ label, desc, href, icon: Icon, color }) => (
                        <Link
                            key={href}
                            href={href}
                            className="group bg-card border border-border rounded-2xl p-5 flex items-center gap-4 hover:border-blue-400 hover:shadow-md transition-all"
                        >
                            <div className={`p-3 rounded-xl shrink-0 ${colorMap[color]}`}>
                                <Icon className="w-6 h-6" />
                            </div>
                            <div className="min-w-0">
                                <p className="font-semibold">{label}</p>
                                <p className="text-sm text-muted-foreground truncate">{desc}</p>
                            </div>
                            <ChevronRight className="ml-auto shrink-0 w-5 h-5 text-muted-foreground group-hover:text-blue-500 transition-colors" />
                        </Link>
                    ))}
                </div>
            </div>
        </div>
    );
}

export default function Home() {
    const { role } = useRole();
    return role === "" ? <LandingView /> : <DashboardView />;
}
