using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlainSight.Server.Services.Versioning;

namespace PlainSight.Server.Api;

public static class VersionApi
{
    public static RouteGroupBuilder MapVersionApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/versions").RequireAuthorization();

        group.MapPost("/refresh", async (
            [FromServices] IPlayerVersionReconciler reconciler,
            [FromServices] Microsoft.Extensions.Logging.ILogger<IPlayerVersionReconciler> logger,
            System.Threading.CancellationToken ct) =>
        {
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

        return group;
    }
}
