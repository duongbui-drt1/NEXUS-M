namespace NexusM.SharedKernel.Audit;

public sealed record BusinessAuditEntry(
    string Action,
    string EntityType,
    string EntityId,
    string Outcome = "Success",
    object? BeforeState = null,
    object? AfterState = null,
    object? Metadata = null);

public interface IBusinessAuditWriter
{
    Task WriteAsync(BusinessAuditEntry entry, CancellationToken ct = default);
}
