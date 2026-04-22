"use client";

import {useSearchParams} from "next/navigation";
import {useEffect} from "react";
import { authService } from "@/services/authService";
import toast from "react-hot-toast";

export default function CallbackPage() {
    const searchParams = useSearchParams();
    const code = searchParams.get("code");

    useEffect(() => {
        if (!code) return;

        const loginWithCode = async () => {
            try {
                await authService.login(code);
                // 使用 hard redirect 確保 server 重新讀取 cookie，RolesProvider 以正確 role 初始化
                window.location.href = "/";
            } catch (e) {
                toast.error("登入失敗");
            }
        };

        loginWithCode();
    }, [code]);


    return <p>Logging in...</p>;
}