using System.Net.Http.Json;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class LogRetentionService(
    LogBuffer buffer,
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    ILogger<LogRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

                if (buffer.IsEmpty)
                {
                    continue;
                }

                string? deviceId = configuration["DeviceId"];
                string? apiKey = configuration["ApiKey"];

                if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(apiKey))
                {
                    continue;
                }

                List<DeviceLogEntryDto> logs = buffer.DequeueAll();
                DeviceLogBatchDto dto = new()
                {
                    DeviceId = deviceId,
                    Logs = logs
                };

                using HttpClient http = httpFactory.CreateClient();
                http.BaseAddress = new Uri(configuration["ServerUrl"] ?? "http://plainsight-server");
                http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

                HttpResponseMessage response = await http.PostAsJsonAsync("/api/device/logs", dto, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Failed to upload logs to server: {StatusCode}", response.StatusCode);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading logs to server");
            }
        }
    }
}
