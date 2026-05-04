namespace PlainSight.Server.Services.Versioning;

public interface IPlayerVersionReconciler
{
    Task<int> ReconcileAsync(CancellationToken ct);
}
