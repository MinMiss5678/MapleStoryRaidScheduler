type TeamSlotCharacter = {
    characterId: string;
    discordId: number;
    discordName: string;
    characterName: string;
    job: string;
    attackPower: number;
}

type TeamSlot = {
    id: number;
    bossName: number;
    slotDateTime: Date;
    characters: TeamSlotCharacter[];
}

export default function RaidResultCard({teamSlot}: { teamSlot: TeamSlot }) {
    return (
        <div className="bg-gray-800 p-5 rounded-xl shadow-md hover:shadow-lg transition w-80">
            <h2 className="text-xl font-bold text-blue-400">{teamSlot.bossName}</h2>
            {/* 標題：排團日期 */}
            <div className="flex justify-between items-center mb-3">
                <h3 className="text-blue-400 font-bold text-lg">
                    {new Date(teamSlot.slotDateTime).toLocaleString("zh-TW", {
                        month: "short",
                        day: "numeric",
                        weekday: "short",
                        hour: "2-digit",
                        minute: "2-digit",
                    })}
                </h3>
            </div>

            {/* 成員清單 */}
            <div className="space-y-2">
                <div className="grid grid-cols-4 text-blue-300 font-semibold border-b border-gray-600 pb-1 text-sm">
                    <span>Discord</span>
                    <span>角色</span>
                    <span>職業</span>
                    <span>攻擊力</span>
                </div>
                {teamSlot.characters.map((m) => (
                    <div
                        key={m.characterId}
                        className="grid grid-cols-4 text-gray-300 border-b border-gray-700 pb-1 text-sm"
                    >
                        <span>{m.discordName}</span>
                        <span>{m.characterName}</span>
                        <span>{m.job}</span>
                        <span>{m.attackPower}</span>
                    </div>
                ))}
            </div>
        </div>
    );
}