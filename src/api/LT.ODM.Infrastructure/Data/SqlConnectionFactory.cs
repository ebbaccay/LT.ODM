using System.Data.Common;
using LT.ODM.Application.Abstractions;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Data;

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
