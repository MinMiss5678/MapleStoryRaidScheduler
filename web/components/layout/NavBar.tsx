"use client"

import {useRouter, usePathname} from "next/navigation";
import {useRole} from "@/app/providers/RolesProvider";
import {useTheme} from "next-themes";
import Link from "next/link";
import { useState, useEffect } from "react";
import { authService } from "@/services/authService";
import { Menu, X, Sun, Moon, LogIn, LogOut, ChevronDown } from "lucide-react";

export default function NavBar() {
    const commonItems = [
        {label: "首頁", href: "/", roles: ["", "user", "admin"]},
        {label: "角色管理", href: "/character", roles: ["user", "admin"]},
        {label: "報名", href: "/register", roles: ["user", "admin"]},
        {label: "補位", href: "/schedule", roles: ["user", "admin"]},
        {label: "排團", href: "/admin/schedule", roles: ["admin"]},
        {label: "排團結果", href: "/scheduleResult", roles: ["user", "admin"]},
    ];

    const adminMenuItems = [
        {label: "範本管理", href: "/admin/templates", roles: ["admin"]},
        {label: "Boss 管理", href: "/admin/boss", roles: ["admin"]},
        {label: "系統設定", href: "/admin/config", roles: ["admin"]},
    ];

    const router = useRouter();
    const pathname = usePathname();
    const [isMenuOpen, setIsMenuOpen] = useState(false);
    const [isAdminMenuOpen, setIsAdminMenuOpen] = useState(false);
    const [mounted, setMounted] = useState(false);
    const {role, setRole} = useRole();
    const {theme, setTheme} = useTheme();

    useEffect(() => {
        setMounted(true);
    }, []);

    const filteredCommonItems = commonItems.filter((item) => item.roles.includes(role));
    const showAdminMenu = role === "admin";

    const handleLogin = async () => {
        router.push("/api/auth/discord");
    };

    const handleLogout = async () => {
        await authService.logout();
        setRole("");
        router.push("/");
    };

    const NavLink = ({ item, mobile = false }: { item: { label: string, href: string }, mobile?: boolean }) => {
        const isActive = pathname === item.href;
        const baseClass = mobile 
            ? "block px-4 py-3 rounded-lg text-base font-medium transition-colors"
            : "px-3 py-2 rounded-lg text-sm font-medium transition-colors relative group";
        
        const activeClass = isActive 
            ? "bg-blue-100 text-blue-600 dark:bg-blue-900/30 dark:text-blue-400" 
            : "text-[var(--foreground)] hover:bg-gray-100 dark:hover:bg-gray-800";

        return (
            <Link 
                href={item.href} 
                onClick={() => setIsMenuOpen(false)}
                className={`${baseClass} ${activeClass}`}
            >
                {item.label}
                {!mobile && isActive && (
                    <span className="absolute bottom-0 left-1/2 -translate-x-1/2 w-1/2 h-0.5 bg-blue-500 rounded-full" />
                )}
            </Link>
        );
    };

    return (
        <nav className="w-full bg-[var(--background)] border-b border-[var(--border-color)] sticky top-0 z-50">
            <div className="max-w-6xl mx-auto px-4">
                <div className="flex items-center justify-between h-16">
                    {/* Logo/Brand */}
                    <Link href="/" className="flex items-center space-x-2 shrink-0">
                        <span className="font-bold text-lg md:text-xl tracking-tight text-[var(--foreground)]">
                            MapleStory Raid Scheduler
                        </span>
                    </Link>

                    {/* Desktop Navigation */}
                    <div className="hidden md:flex items-center space-x-1">
                        {filteredCommonItems.map((item) => (
                            <NavLink key={item.href} item={item} />
                        ))}

                        {showAdminMenu && (
                            <div 
                                className="relative group"
                                onMouseEnter={() => setIsAdminMenuOpen(true)}
                                onMouseLeave={() => setIsAdminMenuOpen(false)}
                            >
                                <button className={`flex items-center space-x-1 px-3 py-2 rounded-lg text-sm font-medium transition-colors text-[var(--foreground)] hover:bg-gray-100 dark:hover:bg-gray-800 ${pathname.startsWith('/admin/') && pathname !== '/admin/schedule' ? 'text-blue-600 dark:text-blue-400' : ''}`}>
                                    <span>管理</span>
                                    <ChevronDown className={`h-4 w-4 transition-transform duration-200 ${isAdminMenuOpen ? 'rotate-180' : ''}`} />
                                </button>
                                
                                {isAdminMenuOpen && (
                                    <div className="absolute top-full left-0 w-48 pt-1 bg-transparent animate-in fade-in zoom-in-95 duration-200">
                                        <div className="py-2 bg-[var(--background)] border border-[var(--border-color)] rounded-xl shadow-xl">
                                            {adminMenuItems.map((item) => (
                                                <Link
                                                    key={item.href}
                                                    href={item.href}
                                                    className={`block px-4 py-2 text-sm font-medium transition-colors hover:bg-gray-100 dark:hover:bg-gray-800 ${pathname === item.href ? 'text-blue-600 dark:text-blue-400' : 'text-[var(--foreground)]'}`}
                                                >
                                                    {item.label}
                                                </Link>
                                            ))}
                                        </div>
                                    </div>
                                )}
                            </div>
                        )}
                        
                        <div className="h-6 w-px bg-[var(--border-color)] mx-2" />
                        
                        <button 
                            onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
                            className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors text-[var(--foreground)]"
                        >
                            {mounted ? (
                                theme === 'dark' ? (
                                    <Sun className="h-5 w-5 text-yellow-500" />
                                ) : (
                                    <Moon className="h-5 w-5 text-slate-700" />
                                )
                            ) : (
                                <div className="h-5 w-5" /> // Placeholder to avoid layout shift
                            )}
                        </button>

                        {role === "" ? (
                            <button onClick={handleLogin}
                                    className="flex items-center space-x-1 ml-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors text-sm font-medium"
                            >
                                <LogIn className="h-4 w-4" />
                                <span>登入</span>
                            </button>
                        ) : (
                            <button onClick={handleLogout}
                                    className="flex items-center space-x-1 ml-2 px-4 py-2 border border-[var(--border-color)] hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg transition-colors text-sm font-medium text-[var(--foreground)]"
                            >
                                <LogOut className="h-4 w-4" />
                                <span>登出</span>
                            </button>
                        )}
                    </div>

                    {/* Mobile Menu Button */}
                    <div className="md:hidden flex items-center space-x-2">
                        <button 
                            onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
                            className="p-2 rounded-lg text-[var(--foreground)]"
                        >
                            {mounted ? (
                                theme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />
                            ) : (
                                <div className="h-5 w-5" /> // Placeholder
                            )}
                        </button>
                        <button 
                            onClick={() => setIsMenuOpen(!isMenuOpen)}
                            className="p-2 rounded-lg text-[var(--foreground)] hover:bg-gray-100 dark:hover:bg-gray-800"
                        >
                            {isMenuOpen ? <X className="h-6 w-6" /> : <Menu className="h-6 w-6" />}
                        </button>
                    </div>
                </div>
            </div>

            {/* Mobile Navigation Drawer */}
            {isMenuOpen && (
                <div className="md:hidden border-t border-[var(--border-color)] bg-[var(--background)] animate-in slide-in-from-top duration-200">
                    <div className="px-2 pt-2 pb-3 space-y-1">
                        {filteredCommonItems.map((item) => (
                            <NavLink key={item.href} item={item} mobile />
                        ))}

                        {showAdminMenu && (
                            <>
                                <div className="px-4 py-2 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                                    管理系統
                                </div>
                                {adminMenuItems.map((item) => (
                                    <NavLink key={item.href} item={item} mobile />
                                ))}
                            </>
                        )}

                        <div className="pt-4 pb-2 border-t border-[var(--border-color)]">
                            {role === "" ? (
                                <button onClick={handleLogin}
                                        className="flex w-full items-center justify-center space-x-2 px-4 py-3 bg-blue-600 text-white rounded-lg font-medium"
                                >
                                    <LogIn className="h-5 w-5" />
                                    <span>登入 Discord</span>
                                </button>
                            ) : (
                                <button onClick={handleLogout}
                                        className="flex w-full items-center justify-center space-x-2 px-4 py-3 border border-[var(--border-color)] text-[var(--foreground)] rounded-lg font-medium"
                                >
                                    <LogOut className="h-5 w-5" />
                                    <span>登出</span>
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </nav>
    );
}