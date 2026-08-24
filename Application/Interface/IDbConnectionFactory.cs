using System.Data.Common;

namespace Application.Interface;

/// <summary>
/// 建立「新的、未開啟」的資料庫連線，集中連線字串這唯一來源。
///
/// 供兩類消費者共用同一份連線設定：
/// 1) scoped <see cref="System.Data.IDbConnection"/>（DbContext/repo，每 scope 一條）；
/// 2) 背景 poller（<c>OutboxDispatcher</c> 等，每批次自開專屬連線、刻意與 request-UoW 脫鉤）。
///
/// 回傳 <see cref="DbConnection"/> 而非具體 Npgsql 型別 → 介面不外洩 Infrastructure，
/// 且 <see cref="DbConnection"/> 具 <c>OpenAsync</c>/<c>BeginTransactionAsync</c> 非同步 API。
/// 延遲開啟（不 eager Open）：交由 DbContext.BeginAsync 或呼叫端在需要時自行開。
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}
