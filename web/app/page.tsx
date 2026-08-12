"use client";

import { useRole } from "@/app/providers/RolesProvider";
import Link from "next/link";
import {
    Zap, Search, Crown, Users, UserCircle, UserCog,
    Swords, LogIn, ChevronRight, Settings
} from "lucide-react";

// ─── 未登入 Landing ───────────────────────────────────────────────
function LandingView() {
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
                    透過 Discord 登入，開隊、找隊、即時揪團。
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

    const userQuickLinks = [
        { label: "即時揪團", desc: "現在就開/找一起打的隊", href: "/teams/instant", icon: Zap, color: "red" },
        { label: "尋隊", desc: "瀏覽開放中的隊伍並申請", href: "/teams/open", icon: Search, color: "blue" },
        { label: "開隊", desc: "自己當隊長、設定需求招人", href: "/teams/new", icon: Crown, color: "amber" },
        { label: "帶隊", desc: "管理我開的隊、審核申請", href: "/me/led-teams", icon: Users, color: "purple" },
        { label: "隊伍列表", desc: "我加入的隊伍與邀請", href: "/me/teams", icon: Swords, color: "green" },
        { label: "我的資料", desc: "常設時段與參戰角色", href: "/register", icon: UserCog, color: "blue" },
        { label: "角色管理", desc: "新增或管理你的角色", href: "/character", icon: UserCircle, color: "orange" },
    ];

    const adminQuickLinks = [
        ...userQuickLinks,
        { label: "Boss 管理", desc: "維護 Boss 清單", href: "/admin/boss", icon: Settings, color: "red" },
        { label: "系統設定", desc: "候選警示等系統參數", href: "/admin/config", icon: Settings, color: "purple" },
    ];

    const quickLinks = role === "admin" ? adminQuickLinks : userQuickLinks;

    const colorMap: Record<string, string> = {
        blue:   "bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400",
        green:  "bg-green-100 dark:bg-green-900/30 text-green-600 dark:text-green-400",
        purple: "bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400",
        orange: "bg-orange-100 dark:bg-orange-900/30 text-orange-600 dark:text-orange-400",
        amber:  "bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400",
        red:    "bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400",
    };

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground">
            <div className="max-w-4xl mx-auto">
                <h1 className="text-3xl font-bold tracking-tight mb-2">總覽</h1>
                <p className="text-muted-foreground mb-8">開隊、找隊、即時揪團——選一個入口開始。</p>

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
