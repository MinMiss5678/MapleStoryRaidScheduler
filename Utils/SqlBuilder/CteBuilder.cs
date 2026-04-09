namespace Utils.SqlBuilder;

public class CteBuilder
{
    private readonly List<string> _ctes = new();

    public CteBuilder With(string name, string sql)
    {
        _ctes.Add($"{name} AS ({sql})");
        return this;
    }

    public string Build(string mainSql)
    {
        if (_ctes.Count == 0)
            return mainSql;

        return $"WITH {string.Join(", ", _ctes)} {mainSql}";
    }
}