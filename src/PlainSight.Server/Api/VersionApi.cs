using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class VersionApi
{
    public static RouteGroupBuilder MapVersionApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/versions");

        // Note: All version management (upload, delete, assignment) is now handled 
        // directly in Versions.razor via database and service access.
        // There are currently no player-facing endpoints in this group.

        return group;
    }
}
