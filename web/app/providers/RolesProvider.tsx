"use client"

import { createContext, useState, ReactNode, useContext } from "react";


interface RoleContextType {
    role: string;
    setRole: (role: string) => void;
}

const RolesContext = createContext<RoleContextType>({
    role: "",
    setRole: () => {}
});

export function RolesProvider({ children, initialRole }: { children: ReactNode, initialRole: string }) {
    const [role, setRole] = useState(initialRole);

    return <RolesContext.Provider value={{ role, setRole }}>{children}</RolesContext.Provider>;
}

export function useRole() {
    return useContext(RolesContext);
}