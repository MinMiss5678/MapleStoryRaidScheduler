using Xunit;

namespace Test.Integration;

/// <summary>
/// 凡是用 <c>WebApplicationFactory&lt;Program&gt;</c> 啟動整個 WebApi 的測試類別，一律放進這個集合（同集合內不平行）。
/// Program.cs 開頭把 Serilog 的 bootstrap logger 設到全域靜態 <c>Log.Logger</c>，建 host 時再 Freeze 它；
/// 兩個 host 同時建置會搶同一個全域 logger → 第二個 Freeze 丟「The logger is already frozen」。
/// 正式環境一個行程只有一個 host，不受影響；這純粹是測試行程內平行建多個 host 的問題。
/// </summary>
[CollectionDefinition("webapp-host", DisableParallelization = true)]
public class WebAppHostCollection;
