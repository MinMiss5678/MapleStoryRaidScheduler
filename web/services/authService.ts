export const authService = {
    async login(code: string): Promise<{ role: string }> {
        const res = await fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ code }),
        });
        if (!res.ok) throw new Error("登入失敗");
        return res.json();
    },

    async logout(): Promise<void> {
        await fetch("/api/auth/logout", {
            method: "POST",
        });
    },
};
