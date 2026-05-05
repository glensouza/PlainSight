using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

internal sealed class LogShipperService(
    HttpClient http,
    HeartbeatService heartbeat,
    LogBuffer buffer,
    ILogger<LogShipperService> logger) : BackgroundService
{
    private readonly string deviceId = Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await this.ShipLogsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
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
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error shipping logs to server");
        }
    }
}
