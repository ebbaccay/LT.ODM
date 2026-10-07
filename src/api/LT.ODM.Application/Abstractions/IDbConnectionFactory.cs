using System.Data.Common;

namespace LT.ODM.Application.Abstractions;

public interface IDbConnectionFactory
{
    /// <summary>Creates and opens a connection to the Style Library database.</summary>
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
