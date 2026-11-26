using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusM.SharedKernel.Context;

namespace NexusM.BuildingBlocks.Persistence;

/// <summary>
/// Binds PostgreSQL session variable app.current_tenant_id on every opened connection
/// so FORCE ROW LEVEL SECURITY policies enforce strict tenant isolation at the DB engine layer (ADR-004).
/// </summary>
public sealed class TenantRlsConnectionInterceptor(IRequestContext requestContext) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantContextAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyTenantContextAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task ApplyTenantContextAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        var tid = requestContext.TenantId?.ToString("D") ?? string.Empty;
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tid, false);";
        var param = cmd.CreateParameter();
        param.ParameterName = "tid";
        param.Value = tid;
        cmd.Parameters.Add(param);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
