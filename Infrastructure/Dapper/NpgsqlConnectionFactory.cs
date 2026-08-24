using System.Data.Common;
using Application.Interface;
using Npgsql;

namespace Infrastructure.Dapper;

/// <inheritdoc cref="IDbConnectionFactory" />
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbConnection Create() => new NpgsqlConnection(_connectionString);
}
