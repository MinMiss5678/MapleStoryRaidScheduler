"use client";

import {useSearchParams, useRouter} from "next/navigation";
import {useEffect} from "react";
import {useRole} from "../providers/RolesProvider";
import { authService } from "@/services/authService";
import toast from "react-hot-toast";

export default function CallbackPage() {
    const router = useRouter();
    const searchParams = useSearchParams();
    const code = searchParams.get("code");
    const {setRole} = useRole();

    useEffect(() => {
        if (!code) return;

        const loginWithCode = async () => {
            try {
                const data = await authService.login(code);
                setRole(data.role.toLowerCase());
                router.push("/");
            } catch (e) {
                toast.error("登入失敗");
            }
        };

        loginWithCode();
    }, [code, router, setRole]);


    return <p>Logging in...</p>;
}