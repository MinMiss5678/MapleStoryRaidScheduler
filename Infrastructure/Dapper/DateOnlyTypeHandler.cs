using System.Data;
using Dapper;

namespace Infrastructure.Dapper;

/// <summary>
/// Dapper/Npgsql 沒有內建 <see cref="DateOnly"/> 的參數處理（會丟 NotSupportedException）。
/// 送出時以 <see cref="DbType.Date"/> 傳乾淨的 date（避免 date 欄位對到 timestamp 的隱式轉型）；
/// 讀回時 Npgsql 給 DateTime。與 <see cref="TimeOnlyTypeHandler"/> 同一套慣例。
/// </summary>
public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value)
    {
        if (value is DateOnly d) return d;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        if (value is DateTimeOffset dto) return DateOnly.FromDateTime(dto.DateTime);
        return DateOnly.Parse(value.ToString()!);
    }
}
