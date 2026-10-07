using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Tms;

/// <summary>Result rows in the TMS "flat" shape: one entry per result set.</summary>
public sealed record FlatResultSet(List<string> Columns, List<object?[]> Rows, int RowCount);

/// <summary>A streamed chunk, same shape as TMS StreamStoredProcFlat.</summary>
public sealed record FlatChunk(List<string> Columns, List<object?[]> Rows, bool IsPartial);

/// <summary>Executes allowlisted TMS procedures. Used only by the TMS compatibility hub.</summary>
public sealed class TmsProcedureExecutor(
    TmsProcedureCatalog catalog,
    IOptions<TmsProcedureOptions> options,
    TmsConnectionString connectionString,
    ILogger<TmsProcedureExecutor> logger)
{
    private static readonly ConcurrentDictionary<string, string[]> ParameterCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TmsProcedureOptions _options = options.Value;

    /// <summary>Errors raised by the procedures themselves (RAISERROR / THROW with number >= 50000) are business messages and pass through.</summary>
    private const int FirstUserErrorNumber = 50000;

    public TmsProcedure Authorize(string? requestedName, ClaimsPrincipal user)
        => catalog.Resolve(requestedName, user) ?? throw new TmsRequestException("This action is not allowed.");

    /// <summary>
    /// TMS ExecuteStoredProcFlat: an object runs once; an array runs once per row (like the TMS hub).
    /// Returns { status = "Success", data } or { status = "Failed", message } exactly as TMS did.
    /// </summary>
    public async Task<object> ExecuteFlatAsync(TmsProcedure procedure, JsonElement? parameters, ClaimsPrincipal user, CancellationToken ct)
    {
        // TMS sent [] for "no parameters".
        if (parameters is { ValueKind: JsonValueKind.Array } empty && empty.GetArrayLength() == 0)
            parameters = null;

        if (parameters is { ValueKind: JsonValueKind.Array } rows)
        {
            if (rows.GetArrayLength() > _options.MaxBatchRows)
                return Failed($"Too many rows in one call (maximum {_options.MaxBatchRows}).");
            var results = new List<object>();
            foreach (var row in rows.EnumerateArray())
                results.Add(await ExecuteOnceAsync(procedure, row, user, ct));
            return results;
        }
        return await ExecuteOnceAsync(procedure, parameters, user, ct);
    }

    private async Task<object> ExecuteOnceAsync(TmsProcedure procedure, JsonElement? parameters, ClaimsPrincipal user, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var conn = new SqlConnection(connectionString.Value);
            await conn.OpenAsync(ct);
            await using var cmd = await CreateCommandAsync(conn, procedure, parameters, user, ct);

            var sets = new List<FlatResultSet>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                do
                {
                    var columns = Columns(reader);
                    var data = new List<object?[]>();
                    while (await reader.ReadAsync(ct)) data.Add(Row(reader));
                    sets.Add(new FlatResultSet(columns, data, data.Count));
                }
                while (await reader.NextResultAsync(ct));
            }

            logger.LogDebug("TMS {Procedure} by {User}: {Sets} result sets in {Elapsed} ms",
                procedure.Name, user.Identity?.Name, sets.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new { status = "Success", data = sets };
        }
        catch (TmsRequestException ex)
        {
            return Failed(ex.Message);
        }
        catch (SqlException ex) when (ex.Number >= FirstUserErrorNumber)
        {
            return Failed(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reference = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            logger.LogError(ex, "TMS {Procedure} failed for {User}. Reference {Reference}", procedure.Name, user.Identity?.Name, reference);
            return Failed($"The request could not be completed. Reference: {reference}");
        }
    }

    /// <summary>TMS StreamStoredProcFlat: rows in chunks of 1,000 per result set.</summary>
    public async IAsyncEnumerable<FlatChunk> StreamFlatAsync(TmsProcedure procedure, JsonElement? parameters, ClaimsPrincipal user, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString.Value);
        await conn.OpenAsync(ct);
        await using var cmd = await CreateCommandAsync(conn, procedure, parameters, user, ct);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        do
        {
            var columns = Columns(reader);
            var chunk = new List<object?[]>();
            while (await reader.ReadAsync(ct))
            {
                chunk.Add(Row(reader));
                if (chunk.Count >= 1000)
                {
                    yield return new FlatChunk(columns, chunk, true);
                    chunk = [];
                }
            }
            if (chunk.Count > 0) yield return new FlatChunk(columns, chunk, false);
        }
        while (await reader.NextResultAsync(ct));
    }

    private async Task<SqlCommand> CreateCommandAsync(SqlConnection conn, TmsProcedure procedure, JsonElement? parameters, ClaimsPrincipal user, CancellationToken ct)
    {
        var declared = await GetDeclaredParametersAsync(conn, procedure, ct);
        var cmd = new SqlCommand(procedure.QualifiedName, conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        cmd.Parameters.AddRange([.. TmsParameterBinder.Bind(procedure, declared, parameters, user)]);
        return cmd;
    }

    /// <summary>Input parameter names of the procedure, cached per process (restart the API after changing a procedure's parameters).</summary>
    private async Task<string[]> GetDeclaredParametersAsync(SqlConnection conn, TmsProcedure procedure, CancellationToken ct)
    {
        if (ParameterCache.TryGetValue(procedure.Name, out var cached)) return cached;

        await using var cmd = new SqlCommand(
            """
            SELECT p.name FROM sys.parameters p
            WHERE p.object_id = OBJECT_ID(@proc, N'P') AND p.is_output = 0 AND p.parameter_id > 0
            """, conn);
        cmd.Parameters.Add(new SqlParameter("@proc", SqlDbType.NVarChar, 300) { Value = procedure.QualifiedName });

        var names = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));

        if (names.Count == 0)
        {
            await using var check = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@proc, N'P') IS NULL THEN 0 ELSE 1 END", conn);
            check.Parameters.Add(new SqlParameter("@proc", SqlDbType.NVarChar, 300) { Value = procedure.QualifiedName });
            if ((int)(await check.ExecuteScalarAsync(ct))! == 0) throw new TmsRequestException($"Procedure {procedure.Name} is not deployed to this database yet.");
        }

        var result = names.ToArray();
        ParameterCache[procedure.Name] = result;
        return result;
    }

    private static List<string> Columns(SqlDataReader reader)
        => Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();

    private static object?[] Row(SqlDataReader reader)
    {
        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);
        for (var i = 0; i < values.Length; i++)
            if (values[i] == DBNull.Value) values[i] = null;
        return values;
    }

    private static object Failed(string message) => new { status = "Failed", message };
}

/// <summary>Connection string for the database holding the ported TMS procedures.</summary>
public sealed record TmsConnectionString(string Value);
