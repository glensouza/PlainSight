using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

internal sealed class LogShipperService(
    HttpClient http,
    HeartbeatService heartbeat,
    LogBuffer buffer,
    LogConfig logConfig,
    ILogger<LogShipperService> logger) : BackgroundService
{
    private readonly string deviceId = Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                int interval = Math.Clamp(logConfig.ShipIntervalSeconds, 10, 3600);
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
                await this.ShipLogsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await this.ShipLogsAsync(cancellationToken);
    }

    private async Task ShipLogsAsync(CancellationToken ct)
    {
        List<DeviceLogEntryDto> entries = buffer.DrainAll();
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            string? apiKey = heartbeat.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                buffer.Requeue(entries);
                return;
            }

            DeviceLogBatchDto batch = new() { Entries = entries };

            using HttpRequestMessage request = new(HttpMethod.Post, $"/api/device/{this.deviceId}/logs");
            request.Content = JsonContent.Create(batch);
            request.Headers.Add("X-Api-Key", apiKey);

            HttpResponseMessage response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Log ship failed: {Status}", response.StatusCode);
                buffer.Requeue(entries);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            buffer.Requeue(entries);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error shipping logs to server");
            buffer.Requeue(entries);
        }
    }
}
