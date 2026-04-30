using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class ScheduleApi
{
    public static RouteGroupBuilder MapScheduleApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/schedules");

        group.MapGet("/", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<Schedule> schedules = await context.Schedules
                .Include(s => s.Playlist)
                .Include(s => s.TargetGroups)
                .OrderBy(s => s.StartTime)
                .ToListAsync(ct);
            
            return Results.Ok(schedules);
        });

        group.MapPost("/", async (ScheduleCreateDto dto, PlainSightDbContext context) =>
        {
            Schedule schedule = new()
            {
                Name = dto.Name,
                PlaylistId = dto.PlaylistId,
                DaysOfWeek = dto.DaysOfWeek,
                ScheduledDate = dto.ScheduledDate,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Priority = dto.Priority,
                IsActive = dto.IsActive
            };

            if (dto.TargetGroups != null)
            {
                foreach (string groupName in dto.TargetGroups)
                {
                    schedule.TargetGroups.Add(new ScheduleTargetGroup { GroupName = groupName });
                }
            }

            context.Schedules.Add(schedule);
            await context.SaveChangesAsync();

            return Results.Ok(schedule);
        });

        group.MapPut("/{id:int}", async (int id, ScheduleCreateDto dto, PlainSightDbContext context) =>
        {
            Schedule? schedule = await context.Schedules
                .Include(s => s.TargetGroups)
                .FirstOrDefaultAsync(s => s.Id == id);
            
            if (schedule == null)
                return Results.NotFound();

            schedule.Name = dto.Name;
            schedule.PlaylistId = dto.PlaylistId;
            schedule.DaysOfWeek = dto.DaysOfWeek;
            schedule.ScheduledDate = dto.ScheduledDate;
            schedule.StartTime = dto.StartTime;
            schedule.EndTime = dto.EndTime;
            schedule.Priority = dto.Priority;
            schedule.IsActive = dto.IsActive;

            // Sync target groups
            schedule.TargetGroups.Clear();
            if (dto.TargetGroups != null)
            {
                foreach (string groupName in dto.TargetGroups)
                {
                    schedule.TargetGroups.Add(new ScheduleTargetGroup { GroupName = groupName });
                }
            }

            await context.SaveChangesAsync();
            return Results.Ok(schedule);
        });

        group.MapDelete("/{id:int}", async (int id, PlainSightDbContext context) =>
        {
            Schedule? schedule = await context.Schedules.FindAsync(id);
            if (schedule == null)
                return Results.NotFound();

            context.Schedules.Remove(schedule);
            await context.SaveChangesAsync();
            return Results.Ok();
        });

        return group;
    }
}

public record ScheduleCreateDto(
    string Name,
    int PlaylistId,
    List<string>? TargetGroups,
    DayOfWeekFlags DaysOfWeek,
    DateOnly? ScheduledDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Priority,
    bool IsActive);
