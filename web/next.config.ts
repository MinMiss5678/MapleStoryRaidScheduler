import type { NextConfig } from "next";

const nextConfig: NextConfig = {
    reactStrictMode: true,
    swcMinify: true,
    // 可在前端直接使用 process.env.NEXT_PUBLIC_API_URL
    env: {
        NEXT_PUBLIC_API_URL: process.env.NEXT_PUBLIC_API_URL
    },
    async rewrites() {
        return [
            {
                source: '/api/:path*',
                // 將前端 /api/* 請求轉發到後端 ASP.NET Core
                destination: `${process.env.NEXT_PUBLIC_API_URL}/api/:path*`,
            },
        ];
    },
};

export default nextConfig;
