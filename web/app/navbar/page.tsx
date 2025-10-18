"use client"

import {useRouter} from "next/navigation";
import {useRole} from "../providers/RolesProvider";
import {useTheme} from "next-themes";

export default function NavBar() {
    const navItems = [
        {label: "首頁", href: "/", roles: ["", "user", "admin"]},
        {label: "角色管理", href: "/character", roles: ["user", "admin"]},
        {label: "報名", href: "/register", roles: ["user", "admin"]},
        {label: "排團", href: "/schedule", roles: ["admin"]},
        {label: "排團結果", href: "/scheduleResult", roles: ["user", "admin"]},
    ];

    const router = useRouter();
    const go = (path: string) => () => router.push(path);

    const ThemeToggle = () => {
        const {theme, setTheme} = useTheme();

        return (
            <button onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
                    className={"hover:opacity-80"}
            >
                切換模式
            </button>
        );
    };

    const {role, setRole} = useRole();

    const handleLogin = async () => {
        const discordAuthUrl = `https://discord.com/api/oauth2/authorize?client_id=${process.env.NEXT_PUBLIC_DISCORD_CLIENT_ID}&redirect_uri=${encodeURIComponent(process.env.NEXT_PUBLIC_APP_URL + "/callback")}&response_type=code&scope=identify`;
        router.push(discordAuthUrl);
    };

    const handleLogout = async () => {
        await fetch("/api/auth/logout", {
            method: "POST"
        })
        setRole("");
        router.push("/");
    };

    return (
        <nav className="w-full shadow-md">
            <div className="max-w-6xl mx-auto px-4">
                <div className="flex items-center justify-end h-14 space-x-4">
                    {navItems
                        .filter((item) => item.roles.includes(role))
                        .map((item) => (
                            <button key={item.href} onClick={go(item.href)}
                                    style={{
                                        backgroundColor: "transparent",
                                        color: "var(--color-text)",
                                    }}
                                    className="px-3 py-2 rounded-lg hover:opacity-80"
                            >
                                {item.label}
                            </button>
                        ))}

                    {role === "" ? (
                        <button onClick={handleLogin}
                                className="px-3 py-2 rounded-lg hover:opacity-80"
                                style={{
                                    backgroundColor: "transparent",
                                    color: "var(--color-text)",
                                }}
                        >
                            登入
                        </button>
                    ) : (
                        <button onClick={handleLogout}
                                className="px-3 py-2 rounded-lg hover:opacity-80"
                                style={{
                                    backgroundColor: "transparent",
                                    color: "var(--color-text)",
                                }}
                        >
                            登出
                        </button>
                    )}
                    <ThemeToggle></ThemeToggle>
                </div>
            </div>
        </nav>
    );
}