using System.Runtime.CompilerServices;
using System.Text.Json;
using LT.ODM.Infrastructure.Tms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LT.ODM.Api.Hubs;

/// <summary>
/// Compatibility hub for the TMS modules ported as-is: same method and event names as the TMS ProcedureHub,
/// so their Angular code keeps working. Differences from TMS:
/// - signed-in users only;
/// - exact procedure names from tms-procedures.json, per module and role (no prefix wildcards);
/// - the client cannot choose the server, database or connection string;
/// - identity parameters (username, created_by, ...) come from the sign-in token;
/// - SQL system errors are logged, not sent to the browser.
/// Temporary: each module moves to typed API endpoints and is then removed from the allowlist.
/// </summary>
[Authorize]
public sealed class TmsProcedureHub(TmsProcedureExecutor executor, ILogger<TmsProcedureHub> logger) : Hub
{
    /// <summary>TMS request shape. ConnectionString is accepted for compatibility and ignored.</summary>
    public sealed record SpRequest(string? SpName, JsonElement? Parameters, string? ConnectionString = null);

    /// <summary>Replies with the "StoredProcResultFlat" event, like TMS.</summary>
    public async Task ExecuteStoredProcFlat(SpRequest request)
    {
        var procedure = Authorize(request);
        var result = await executor.ExecuteFlatAsync(procedure, request.Parameters, Context.User!, Context.ConnectionAborted);
        // Echo the name exactly as sent: TMS screens filter replies on it.
        await Clients.Caller.SendAsync("StoredProcResultFlat", new { procedure = request.SpName, data = result });
    }

    public async IAsyncEnumerable<FlatChunk> StreamStoredProcFlat(SpRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var procedure = Authorize(request);
        await using var chunks = executor.StreamFlatAsync(procedure, request.Parameters, Context.User!, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await chunks.MoveNextAsync();
            }
            catch (TmsRequestException ex)
            {
                throw new HubException(ex.Message);
            }
            if (!hasNext) yield break;
            yield return chunks.Current;
        }
    }

    private TmsProcedure Authorize(SpRequest request)
    {
        if (!string.IsNullOrEmpty(request.ConnectionString))
            logger.LogWarning("Ignored a client-supplied connection string from {User} for {Procedure}.", Context.User?.Identity?.Name, request.SpName);

        try
        {
            return executor.Authorize(request.SpName, Context.User!);
        }
        catch (TmsRequestException ex)
        {
            logger.LogWarning("Refused TMS procedure {Procedure} for {User}.", request.SpName, Context.User?.Identity?.Name);
            throw new HubException(ex.Message);
        }
    }
}
