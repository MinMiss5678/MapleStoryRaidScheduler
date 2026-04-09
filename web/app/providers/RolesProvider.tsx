"use client"

import { createContext, useState, ReactNode, useContext } from "react";

export type Role = "admin" | "user" | "";

interface RoleContextType {
    role: Role;
    setRole: (role: Role) => void;
}

const RolesContext = createContext<RoleContextType>({
    role: "",
    setRole: () => {}
});

export function RolesProvider({ children, initialRole }: { children: ReactNode, initialRole: Role }) {
    const [role, setRole] = useState<Role>(initialRole);

    return <RolesContext.Provider value={{ role, setRole }}>{children}</RolesContext.Provider>;
}

export function useRole() {
    return useContext(RolesContext);
}
