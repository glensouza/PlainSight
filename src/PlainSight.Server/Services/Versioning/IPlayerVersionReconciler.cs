using System.Threading;
using System.Threading.Tasks;

namespace PlainSight.Server.Services.Versioning;

public interface IPlayerVersionReconciler
{
    Task<int> ReconcileAsync(CancellationToken ct);
}
