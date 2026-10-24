namespace BakeryPOS.Application.Common.Interfaces;

/// <summary>
/// Dapper-based read service for optimized queries (CQRS read side).
/// </summary>
public interface IDapperQueryService
{
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);

    /// <summary>
    /// Execute a stored procedure and return results.
    /// </summary>
    Task<IReadOnlyList<T>> ExecuteStoredProcedureAsync<T>(string procedureName, object? parameters = null, CancellationToken ct = default);
}
