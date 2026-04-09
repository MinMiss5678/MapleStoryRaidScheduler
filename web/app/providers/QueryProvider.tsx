"use client";

import { QueryClient, QueryClientProvider, QueryCache, MutationCache } from "@tanstack/react-query";
import { useState } from "react";
import toast from "react-hot-toast";
import { ApiError } from "@/services/apiClient";

/** 404 是「查無資料」的正常狀態，不顯示 Toast */
function shouldShowToast(error: unknown): boolean {
    if (error instanceof ApiError && error.status === 404) return false;
    return true;
}

function getErrorMessage(error: unknown): string {
    if (error instanceof Error) return error.message;
    return "發生未預期的錯誤";
}

export default function QueryProvider({ children }: { children: React.ReactNode }) {
    const [queryClient] = useState(
        () =>
            new QueryClient({
                queryCache: new QueryCache({
                    onError: (error) => {
                        if (shouldShowToast(error)) {
                            toast.error(getErrorMessage(error));
                        }
                    },
                }),
                mutationCache: new MutationCache({
                    onError: (error) => {
                        if (shouldShowToast(error)) {
                            toast.error(getErrorMessage(error));
                        }
                    },
                }),
                defaultOptions: {
                    queries: {
                        staleTime: 60 * 1000,
                        retry: 1,
                    },
                },
            })
    );

    return (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    );
}
