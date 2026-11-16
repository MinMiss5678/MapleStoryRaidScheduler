using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// 正式環境的 Redis 連線設定。Program.cs 與整合測試共用這一份，避免測試自己設一套而與正式不符。
/// </summary>
public static class RedisConnectionOptions
{
    public static ConfigurationOptions Build(string configuration)
    {
        var options = ConfigurationOptions.Parse(configuration);
        // Redis 掛也不擋 app 啟動；三個 Redis 元件（冪等、session 快取、限流）不可用時 fail-open
        options.AbortOnConnectFail = false;
        // fail-open 要「立刻」放行，不是等逾時才放行：預設 BacklogPolicy 會把指令排隊等重連、等滿 5 秒才失敗，
        // 一個寫入請求依序經過限流、冪等（、session 快取），Redis 掛掉時最壞要等十幾秒。
        // FailFast：連線不健康時立刻失敗 → 立刻 fail-open。
        options.BacklogPolicy = BacklogPolicy.FailFast;
        // 連線還在但 Redis 卡住時的上限；同機房的 Redis 指令通常 < 1 ms。偶爾超過會暫時 fail-open，
        // 三者真正的保證都在別處（DB 約束／advisory lock、DB 為 session 真實來源）。
        options.AsyncTimeout = 500;
        options.SyncTimeout = 500;
        // 只影響（背景）建立／重建連線，不擋請求
        options.ConnectTimeout = 2000;
        return options;
    }
}
