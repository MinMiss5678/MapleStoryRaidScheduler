using System.Data;
using Dapper;

namespace Infrastructure.Dapper;

public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.Value = value.ToTimeSpan();
    }

    public override TimeOnly Parse(object value)
    {
        if (value is TimeSpan ts)
        {
            return TimeOnly.FromTimeSpan(ts);
        }

        if (value is DateTime dt)
        {
            return TimeOnly.FromDateTime(dt);
        }

        return TimeOnly.Parse(value.ToString());
    }
}
