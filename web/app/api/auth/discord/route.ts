import { NextResponse } from "next/server";

export async function GET() {
    const clientId = process.env.NEXT_PUBLIC_DISCORD_CLIENT_ID;
    const appUrl = process.env.NEXT_PUBLIC_APP_URL;

    const redirectUri = `${appUrl}/callback`;

    const discordAuthUrl =
        `https://discord.com/api/oauth2/authorize` +
        `?client_id=${clientId}` +
        `&redirect_uri=${encodeURIComponent(redirectUri)}` +
        `&response_type=code` +
        `&scope=identify`;

    return NextResponse.redirect(discordAuthUrl);
}