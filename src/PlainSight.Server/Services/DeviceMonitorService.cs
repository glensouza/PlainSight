using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

internal sealed class DeviceMonitorService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DeviceMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AlertsOptions options = configuration.GetSection("Alerts").Get<AlertsOptions>() ?? new AlertsOptions();

        if (!options.Enabled)
        {
            logger.LogInformation("DeviceMonitorService: alerts disabled via configuration");
            return;
        }

        logger.LogInformation("DeviceMonitorService started; checking every 60s, offline threshold = {Minutes} min", options.OfflineThresholdMinutes);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await this.CheckDevicesAsync(options, stoppingToken);
        }
    }

    private async Task CheckDevicesAsync(AlertsOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            PlainSightDbContext context = scope.ServiceProvider.GetRequiredService<PlainSightDbContext>();

            DateTime offlineThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(options.OfflineThresholdMinutes);

            List<Device> newlyOffline = await context.Devices
                .Where(d => d.LastSeen < offlineThreshold && !d.IsAlertSent)
                .ToListAsync(cancellationToken);

            foreach (Device device in newlyOffline)
            {
                bool sent = await this.TrySendEmailAsync(options, BuildOfflineSubject(device),
                    BuildOfflineBody(device, options));
                if (!sent)
                {
                    continue;
                }

                device.IsAlertSent = true;
                logger.LogInformation("Offline alert sent for device {DeviceId} ({Name})", device.DeviceId,
                    device.Name);
            }

            List<Device> recovered = await context.Devices
                .Where(d => d.LastSeen >= offlineThreshold && d.IsAlertSent)
                .ToListAsync(cancellationToken);

            foreach (Device device in recovered)
            {
                bool sent = await this.TrySendEmailAsync(options, BuildOnlineSubject(device), BuildOnlineBody(device));
                if (!sent)
                {
                    continue;
                }

                device.IsAlertSent = false;
                logger.LogInformation("Recovery email sent for device {DeviceId} ({Name})", device.DeviceId,
                    device.Name);
            }

            List<Device> stallCandidates = await context.Devices
                .Where(d => d.LastSeen >= offlineThreshold
                    && d.CurrentlyPlayingExpectedDurationSeconds != null
                    && d.CurrentlyPlayingSince != null)
                .ToListAsync(cancellationToken);

            foreach (Device device in stallCandidates.Where(d => !d.IsStalledAlertSent && IsStalled(d, options.StalledMultiplier)))
            {
                bool sent = await this.TrySendEmailAsync(options, BuildStalledSubject(device), BuildStalledBody(device, options));
                if (!sent)
                {
                    continue;
                }

                device.IsStalledAlertSent = true;
                logger.LogInformation("Stalled alert sent for device {DeviceId} ({Name})", device.DeviceId, device.Name);
            }

            foreach (Device device in stallCandidates.Where(d => d.IsStalledAlertSent && !IsStalled(d, options.StalledMultiplier)))
            {
                bool sent = await this.TrySendEmailAsync(options, BuildStalledRecoverySubject(device), BuildStalledRecoveryBody(device));
                if (!sent)
                {
                    continue;
                }

                device.IsStalledAlertSent = false;
                logger.LogInformation("Device {DeviceId} ({Name}) recovered from stalled playback", device.DeviceId, device.Name);
            }

            // A device that goes offline (or loses its tracked playback item) while flagged as stalled
            // would otherwise never clear the flag, since it drops out of stallCandidates above.
            List<Device> staleStalledFlags = await context.Devices
                .Where(d => d.IsStalledAlertSent
                    && (d.LastSeen < offlineThreshold || d.CurrentlyPlayingExpectedDurationSeconds == null || d.CurrentlyPlayingSince == null))
                .ToListAsync(cancellationToken);

            foreach (Device device in staleStalledFlags)
            {
                device.IsStalledAlertSent = false;
            }

            if (newlyOffline.Count > 0 || recovered.Count > 0 || stallCandidates.Count > 0 || staleStalledFlags.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, no action needed
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeviceMonitorService: error during device check");
        }
    }

    private async Task<bool> TrySendEmailAsync(AlertsOptions options, string subject, string body)
    {
        AlertEmailOptions email = options.Email;

        if (string.IsNullOrWhiteSpace(email.SmtpHost) || string.IsNullOrWhiteSpace(email.To) || string.IsNullOrWhiteSpace(email.Username))
        {
            logger.LogDebug("DeviceMonitorService: SMTP not configured, skipping email send");
            return true;
        }

        try
        {
            using SmtpClient smtp = new(email.SmtpHost, email.SmtpPort);
            smtp.EnableSsl = true;
            smtp.Credentials = new NetworkCredential(email.Username, email.Password);

            string fromAddress = string.IsNullOrWhiteSpace(email.From) ? email.Username : email.From;
            using MailMessage message = new(fromAddress, email.To, subject, body);

            await smtp.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeviceMonitorService: failed to send email to {To}", email.To);
            return false;
        }
    }

    private static string BuildOfflineSubject(Device device) => $"[PlainSight] Device {device.Name} is OFFLINE";

    private static string BuildOfflineBody(Device device, AlertsOptions options) =>
        $"""
        A PlainSight device has gone offline.

        Device Name : {device.Name}
        Device ID   : {device.DeviceId}
        Group       : {device.Group}
        Last Seen   : {device.LastSeen:u} UTC
        Threshold   : {options.OfflineThresholdMinutes} minutes

        Log in to the PlainSight dashboard to investigate.
        """;

    private static string BuildOnlineSubject(Device device) =>
        $"[PlainSight] Device {device.Name} is back ONLINE";

    private static string BuildOnlineBody(Device device) =>
        $"""
        A PlainSight device has recovered and is back online.

        Device Name : {device.Name}
        Device ID   : {device.DeviceId}
        Group       : {device.Group}
        Last Seen   : {device.LastSeen:u} UTC
        """;

    private static bool IsStalled(Device device, double multiplier)
    {
        if (device.CurrentlyPlayingExpectedDurationSeconds is not > 0 || device.CurrentlyPlayingSince is null)
        {
            return false;
        }

        TimeSpan elapsed = DateTime.UtcNow - device.CurrentlyPlayingSince.Value;
        return elapsed.TotalSeconds > device.CurrentlyPlayingExpectedDurationSeconds.Value * multiplier;
    }

    private static string BuildStalledSubject(Device device) =>
        $"[PlainSight] Device {device.Name} playback appears STALLED";

    private static string BuildStalledBody(Device device, AlertsOptions options) =>
        $"""
        A PlainSight device's playback hasn't advanced past its expected duration.

        Device Name    : {device.Name}
        Device ID      : {device.DeviceId}
        Group          : {device.Group}
        Current File   : {device.CurrentlyPlaying}
        Playing Since  : {device.CurrentlyPlayingSince:u} UTC
        Expected (sec) : {device.CurrentlyPlayingExpectedDurationSeconds}
        Threshold      : {options.StalledMultiplier}x expected duration

        Consider rebooting the device via the dashboard.
        """;

    private static string BuildStalledRecoverySubject(Device device) =>
        $"[PlainSight] Device {device.Name} playback RECOVERED";

    private static string BuildStalledRecoveryBody(Device device) =>
        $"""
        A PlainSight device's playback has resumed advancing normally.

        Device Name   : {device.Name}
        Device ID     : {device.DeviceId}
        Group         : {device.Group}
        Current File  : {device.CurrentlyPlaying}
        """;
}
