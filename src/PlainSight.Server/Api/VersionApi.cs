using Microsoft.AspNetCore.Mvc;
using PlainSight.Server.Services.Versioning;

namespace PlainSight.Server.Api;

public static class VersionApi
{
    public static void MapVersionApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/versions").RequireAuthorization();

        // Manual refresh for external callers/scripts (curl, CI, etc.). UI uses reconciler directly via DI.
        group.MapPost("/refresh", async (
            [FromServices] IPlayerVersionReconciler reconciler,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("VersionApi");
            try
            {
                int count = await reconciler.ReconcileAsync(ct);
                return Results.Ok(new { ingested = count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciler threw an exception during manual refresh.");
                return Results.StatusCode(503);
            }
        });
    }
}
