using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

/// <summary>
/// Connects to OBS via WebSocket v5 (built into OBS 28+) to detect when the NDI Output
/// is active. Updates NdiSource.LastSeenUtc so the existing heartbeat staleness window
/// drives device live-mode switching automatically.
///
/// Configure via appsettings / environment:
///   OBS__WebSocketUrl      ws://192.168.1.50:4455
///   OBS__WebSocketPassword (optional)
///   OBS__NdiOutputName     (optional override — auto-detected by default)
///   OBS__NdiSourceName     CHURCH-PC (Sanctuary-Livestream)
/// </summary>
public class OBSDiscoveryService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<OBSDiscoveryService> logger) : BackgroundService
{
    private const int OpHello = 0;
    private const int OpIdentify = 1;
    private const int OpIdentified = 2;
    private const int OpEvent = 5;
    private const int OpRequest = 6;
    private const int OpRequestResponse = 7;

    private const int StreamingEventSubscription = 16;
    private const int RecordingEventSubscription = 32;
    private const int OutputsEventSubscription = 64;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["OBS:WebSocketUrl"]);
    public bool IsConnected { get; private set; }
    public bool IsNdiOutputActive { get; private set; }
    public bool IsStreaming { get; private set; }
    public bool IsRecording { get; private set; }

    public string ConnectionStatus { get; private set; } = "Not configured";
    public string? ConfiguredNdiSourceName => configuration["OBS:NdiSourceName"];

    private bool SyncWithStreaming => configuration.GetValue("OBS:SyncWithStreaming", false);
    private bool SyncWithRecording => configuration.GetValue("OBS:SyncWithRecording", false);

    // Resolved during each session from GetOutputList; shared across HandleMessageAsync calls
    private string? _resolvedNdiOutputName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? url = configuration["OBS:WebSocketUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            ConnectionStatus = "Not configured (set OBS:WebSocketUrl)";
            logger.LogInformation("OBS discovery disabled — set OBS:WebSocketUrl to enable.");
            return;
        }

        string? configuredOutputName = configuration["OBS:NdiOutputName"];
        string? password = configuration["OBS:WebSocketPassword"];
        string? ndiSourceName = configuration["OBS:NdiSourceName"];

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(url, password, configuredOutputName, ndiSourceName, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                IsNdiOutputActive = false;
                IsStreaming = false;
                IsRecording = false;
                ConnectionStatus = $"Error: {ex.Message}";
                logger.LogWarning("OBS WebSocket session ended: {Message}. Retrying in 10s...", ex.Message);

                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        IsConnected = false;
        IsNdiOutputActive = false;
        IsStreaming = false;
        IsRecording = false;
        ConnectionStatus = "Stopped";
    }

    private async Task RunSessionAsync(
        string url, string? password, string? configuredOutputName, string? ndiSourceName,
        CancellationToken cancellationToken)
    {
        _resolvedNdiOutputName = null;

        using ClientWebSocket ws = new();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        
        logger.LogInformation("OBS WebSocket connecting to {Url}...", url);
        await ws.ConnectAsync(new Uri(url), cancellationToken);

        using (JsonDocument hello = await ReceiveMessageAsync(ws, cancellationToken))
        {
            if (!IsOp(hello, OpHello))
                throw new InvalidOperationException("Expected Hello (op 0) from OBS WebSocket.");

            string authToken = ComputeAuthToken(hello, password);
            int subscriptionMask = OutputsEventSubscription;
            if (SyncWithStreaming) subscriptionMask |= StreamingEventSubscription;
            if (SyncWithRecording) subscriptionMask |= RecordingEventSubscription;

            await SendMessageAsync(ws, BuildIdentifyMessage(authToken, subscriptionMask), cancellationToken);
        }

        using (JsonDocument identified = await ReceiveMessageAsync(ws, cancellationToken))
        {
            if (!IsOp(identified, OpIdentified))
                throw new InvalidOperationException("OBS WebSocket authentication failed — check the password.");
        }

        IsConnected = true;
        ConnectionStatus = $"Connected ({url})";
        logger.LogInformation("OBS WebSocket connected and identified.");

        // Enumerate all outputs so we can auto-detect the NDI output name.
        await SendMessageAsync(ws, BuildRequest("GetOutputList", "init-list", new { }), cancellationToken);

        // Also query streaming/recording status if enabled
        if (SyncWithStreaming)
            await SendMessageAsync(ws, BuildRequest("GetStreamStatus", "init-stream", new { }), cancellationToken);
        if (SyncWithRecording)
            await SendMessageAsync(ws, BuildRequest("GetRecordStatus", "init-record", new { }), cancellationToken);

        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                // Wait for messages with a timeout to allow periodic status refreshes even if no events occur
                using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(30));
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, receiveTimeout.Token);
                
                using JsonDocument msg = await ReceiveMessageAsync(ws, linkedCts.Token);
                await HandleMessageAsync(msg, ws, configuredOutputName, ndiSourceName, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Periodic refresh
                if (IsLiveActive())
                    await TouchNdiSourceAsync(ndiSourceName, cancellationToken);

                if (!string.IsNullOrWhiteSpace(_resolvedNdiOutputName))
                    await SendMessageAsync(ws, BuildRequest("GetOutputStatus", "poll",
                        new { outputName = _resolvedNdiOutputName }), cancellationToken);

                if (SyncWithStreaming)
                    await SendMessageAsync(ws, BuildRequest("GetStreamStatus", "poll-stream", new { }), cancellationToken);
                if (SyncWithRecording)
                    await SendMessageAsync(ws, BuildRequest("GetRecordStatus", "poll-record", new { }), cancellationToken);
            }
        }
        
        if (ws.State != WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"WebSocket closed prematurely (State: {ws.State})");
        }
    }

    public bool IsLiveActive()
    {
        if (IsNdiOutputActive) return true;
        if (SyncWithStreaming && IsStreaming) return true;
        if (SyncWithRecording && IsRecording) return true;
        return false;
    }

    private async Task HandleMessageAsync(
        JsonDocument msg, ClientWebSocket ws,
        string? configuredOutputName, string? ndiSourceName,
        CancellationToken cancellationToken)
    {
        if (!msg.RootElement.TryGetProperty("op", out JsonElement opEl) ||
            !msg.RootElement.TryGetProperty("d", out JsonElement d))
            return;

        int op = opEl.GetInt32();

        if (!d.TryGetProperty("requestType", out JsonElement rtEl))
        {
            // Not a request-response — check for events
            await HandleEventAsync(msg, op, d, ndiSourceName, cancellationToken);
            return;
        }

        string? requestType = rtEl.GetString();

        if (requestType == "GetOutputList")
        {
            await HandleOutputListAsync(d, ws, configuredOutputName, cancellationToken);
        }
        else if (requestType == "GetOutputStatus")
        {
            HandleOutputStatusResponse(d);
        }
        else if (requestType == "GetStreamStatus")
        {
            if (d.TryGetProperty("responseData", out JsonElement respData) &&
                respData.TryGetProperty("outputActive", out JsonElement activeEl))
            {
                IsStreaming = activeEl.GetBoolean();
                logger.LogInformation("OBS streaming is {State}", IsStreaming ? "ACTIVE" : "inactive");
            }
        }
        else if (requestType == "GetRecordStatus")
        {
            if (d.TryGetProperty("responseData", out JsonElement respData) &&
                respData.TryGetProperty("outputActive", out JsonElement activeEl))
            {
                IsRecording = activeEl.GetBoolean();
                logger.LogInformation("OBS recording is {State}", IsRecording ? "ACTIVE" : "inactive");
            }
        }

        if (IsLiveActive())
            await TouchNdiSourceAsync(ndiSourceName, cancellationToken);
    }

    private async Task HandleOutputListAsync(
        JsonElement d, ClientWebSocket ws, string? configuredOutputName,
        CancellationToken cancellationToken)
    {
        if (!d.TryGetProperty("responseData", out JsonElement respData) ||
            !respData.TryGetProperty("outputs", out JsonElement outputs))
        {
            logger.LogWarning("OBS GetOutputList returned no data — cannot auto-detect NDI output.");
            return;
        }

        logger.LogInformation("OBS outputs discovered:");
        string? autoDetectedName = null;

        foreach (JsonElement output in outputs.EnumerateArray())
        {
            string name = output.TryGetProperty("outputName", out JsonElement n) ? n.GetString() ?? "" : "";
            string kind = output.TryGetProperty("outputKind", out JsonElement k) ? k.GetString() ?? "" : "";
            bool active = output.TryGetProperty("outputActive", out JsonElement a) && a.GetBoolean();

            logger.LogInformation("  [{Active}] name={Name}  kind={Kind}",
                active ? "ACTIVE" : "      ", name, kind);

            // Auto-detect: prefer the configured name if it matches, otherwise find any NDI output
            bool isNdi = kind.Contains("ndi", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("ndi", StringComparison.OrdinalIgnoreCase);

            if (isNdi && autoDetectedName == null)
                autoDetectedName = name;

            if (!string.IsNullOrWhiteSpace(configuredOutputName) &&
                string.Equals(name, configuredOutputName, StringComparison.OrdinalIgnoreCase))
            {
                autoDetectedName = name; // explicit config match wins
            }
        }

        if (autoDetectedName != null)
        {
            _resolvedNdiOutputName = autoDetectedName;
            logger.LogInformation("OBS: using NDI output '{Name}'", autoDetectedName);
        }
        else
        {
            logger.LogWarning(
                "OBS: no NDI output found in the list above. " +
                "If OBS-NDI is installed, enable Main Output via Tools → NDI Output Settings. " +
                "You can also set OBS__NdiOutputName to the exact output name shown above.");
        }

        // Now that we know the name, ask for the current state
        if (!string.IsNullOrWhiteSpace(_resolvedNdiOutputName))
        {
            await SendMessageAsync(ws, BuildRequest("GetOutputStatus", "init-status",
                new { outputName = _resolvedNdiOutputName }), cancellationToken);
        }
    }

    private void HandleOutputStatusResponse(JsonElement d)
    {
        // Check for request failure first (e.g. output not found → code 600)
        if (d.TryGetProperty("requestStatus", out JsonElement status) &&
            status.TryGetProperty("result", out JsonElement result) &&
            !result.GetBoolean())
        {
            int code = status.TryGetProperty("code", out JsonElement c) ? c.GetInt32() : 0;
            logger.LogWarning(
                "OBS GetOutputStatus failed (code {Code}) for output '{Name}'. " +
                "Check server logs for the output list printed at connection time.",
                code, _resolvedNdiOutputName);
            return;
        }

        if (!d.TryGetProperty("responseData", out JsonElement respData) ||
            !respData.TryGetProperty("outputActive", out JsonElement activeEl))
            return;

        bool active = activeEl.GetBoolean();
        IsNdiOutputActive = active;
        logger.LogInformation("OBS NDI output '{Name}' is {State}",
            _resolvedNdiOutputName, active ? "ACTIVE" : "inactive");
    }

    private async Task HandleEventAsync(
        JsonDocument msg, int op, JsonElement d, string? ndiSourceName,
        CancellationToken cancellationToken)
    {
        if (op != OpEvent) return;
        if (!d.TryGetProperty("eventType", out JsonElement evTypeEl)) return;
        string eventType = evTypeEl.GetString() ?? string.Empty;

        if (!d.TryGetProperty("eventData", out JsonElement evData)) return;

        if (eventType == "OutputStateChanged")
        {
            if (!evData.TryGetProperty("outputName", out JsonElement outNameEl) ||
                !evData.TryGetProperty("outputActive", out JsonElement evActiveEl))
                return;

            string eventOutputName = outNameEl.GetString() ?? string.Empty;

            // Match against the resolved name (or any output containing "ndi" if not yet resolved)
            bool isOurOutput = !string.IsNullOrWhiteSpace(_resolvedNdiOutputName)
                ? string.Equals(eventOutputName, _resolvedNdiOutputName, StringComparison.OrdinalIgnoreCase)
                : eventOutputName.Contains("ndi", StringComparison.OrdinalIgnoreCase);

            if (!isOurOutput) return;

            // Update resolved name from the event if we didn't have it yet
            if (string.IsNullOrWhiteSpace(_resolvedNdiOutputName))
                _resolvedNdiOutputName = eventOutputName;

            IsNdiOutputActive = evActiveEl.GetBoolean();
            logger.LogInformation("OBS NDI Output '{Name}' changed: {State}",
                eventOutputName, IsNdiOutputActive ? "ACTIVE" : "inactive");
        }
        else if (eventType == "StreamStateChanged" && SyncWithStreaming)
        {
            if (evData.TryGetProperty("outputActive", out JsonElement activeEl))
            {
                IsStreaming = activeEl.GetBoolean();
                logger.LogInformation("OBS streaming changed: {State}", IsStreaming ? "ACTIVE" : "inactive");
            }
        }
        else if (eventType == "RecordStateChanged" && SyncWithRecording)
        {
            if (evData.TryGetProperty("outputActive", out JsonElement activeEl))
            {
                IsRecording = activeEl.GetBoolean();
                logger.LogInformation("OBS recording changed: {State}", IsRecording ? "ACTIVE" : "inactive");
            }
        }

        if (IsLiveActive())
        {
            await TouchNdiSourceAsync(ndiSourceName, cancellationToken);
        }
    }

    private async Task TouchNdiSourceAsync(string? sourceName, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);

        NdiSource? source = null;

        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            source = await context.NdiSources
                .FirstOrDefaultAsync(s => s.ServiceName == sourceName, cancellationToken);
        }

        // Fallback: if no specific source named or found, touch the default source
        if (source == null)
        {
            source = await context.NdiSources
                .FirstOrDefaultAsync(s => s.IsDefault, cancellationToken);
        }

        if (source == null && !string.IsNullOrWhiteSpace(sourceName))
        {
            context.NdiSources.Add(new NdiSource
            {
                ServiceName = sourceName,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                IsManual = true,
                IsDefault = true // Auto-create first OBS source as default
            });
            logger.LogInformation("OBS: auto-created NDI source entry '{SourceName}' as default", sourceName);
        }
        else if (source != null)
        {
            source.LastSeenUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string ComputeAuthToken(JsonDocument hello, string? password)
    {
        if (!hello.RootElement.TryGetProperty("d", out JsonElement d) ||
            !d.TryGetProperty("authentication", out JsonElement auth) ||
            string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        string challenge = auth.GetProperty("challenge").GetString() ?? string.Empty;
        string salt = auth.GetProperty("salt").GetString() ?? string.Empty;

        string secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static string BuildIdentifyMessage(string authToken, int eventSubscriptions)
    {
        if (string.IsNullOrEmpty(authToken))
        {
            return JsonSerializer.Serialize(new
            {
                op = OpIdentify,
                d = new { rpcVersion = 1, eventSubscriptions }
            });
        }

        return JsonSerializer.Serialize(new
        {
            op = OpIdentify,
            d = new { rpcVersion = 1, authentication = authToken, eventSubscriptions }
        });
    }

    private static string BuildRequest(string requestType, string requestId, object requestData)
    {
        return JsonSerializer.Serialize(new
        {
            op = OpRequest,
            d = new { requestType, requestId, requestData }
        });
    }

    private static bool IsOp(JsonDocument msg, int expected)
    {
        return msg.RootElement.TryGetProperty("op", out JsonElement op) && op.GetInt32() == expected;
    }

    private static async Task SendMessageAsync(ClientWebSocket ws, string json, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonDocument> ReceiveMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer for safety
        using MemoryStream ms = new();

        WebSocketReceiveResult result;
        do
        {
            if (ws.State != WebSocketState.Open && ws.State != WebSocketState.CloseReceived)
            {
                throw new InvalidOperationException($"WebSocket is in an invalid state for receive: {ws.State}");
            }

            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("OBS WebSocket closed by server.");
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Seek(0, SeekOrigin.Begin);
        return await JsonDocument.ParseAsync(ms, cancellationToken: ct);
    }
}
