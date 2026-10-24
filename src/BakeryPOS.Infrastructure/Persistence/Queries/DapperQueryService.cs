using System.Data;
using BakeryPOS.Application.Common.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BakeryPOS.Infrastructure.Persistence.Queries;

public sealed class DapperQueryService : IDapperQueryService
{
    private readonly string _connectionString;

    public DapperQueryService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        var result = await connection.QueryAsync<T>(sql, parameters);
        return result.ToList().AsReadOnly();
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        return await connection.ExecuteAsync(sql, parameters);
    }

    public async Task<IReadOnlyList<T>> ExecuteStoredProcedureAsync<T>(string procedureName, object? parameters = null, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        var result = await connection.QueryAsync<T>(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure);
        return result.ToList().AsReadOnly();
    }
}
