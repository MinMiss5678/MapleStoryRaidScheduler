"use client";

import {useSearchParams, useRouter} from "next/navigation";
import {useEffect, useState} from "react";
import {useRole} from "../providers/RolesProvider";

export default function CallbackPage() {
    const router = useRouter();
    const searchParams = useSearchParams();
    const code = searchParams.get("code");
    const {setRole} = useRole();
    const [errorMessage, setErrorMessage] = useState("");

    useEffect(() => {
        if (!code) return;

        const loginWithCode = async () => {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify({code}),
                credentials: "include",
            });

            if (!res.ok) {
                setErrorMessage(`登入失敗`);
                return;
            }

            const data = await res.json();

            if (data.type === "session") setRole("admin");
            else if (data.type === "jwt") setRole("user");

            router.push("/");
        };

        loginWithCode();
    }, [code, router, setRole]);


    if (errorMessage) {
        return (
            <div className="bg-black text-white min-h-screen flex flex-col justify-center items-center gap-4">
                <p>{errorMessage}</p>
            </div>
        );
    }

    return <p>Logging in...</p>;
}