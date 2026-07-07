using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlainSight.Server.Data;
using PlainSight.Server.Models;

namespace PlainSight.Server.Api;

/// <summary>
/// Bridges an admin browser's WebSocket to a player's raw VNC (wayvnc) TCP socket so noVNC can
/// render the live signage in-page. This is a dumb byte pipe: the wayvnc RSA-AES/TLS handshake
/// stays end-to-end between noVNC and the player, so no player-side credentials pass through here.
/// </summary>
public static class VncProxyApi
{
    private const int VncPort = 5900;
    private const int BufferSize = 32 * 1024;

    public static void MapVncProxy(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/device");

        // GET is the HTTP/1.1 WebSocket upgrade; CONNECT is the HTTP/2 Extended CONNECT form
        // browsers use over HTTPS. Accept both so the endpoint matches regardless of protocol.
        group.MapMethods("/{deviceId}/vnc", ["GET", "CONNECT"], async (string deviceId, HttpContext httpContext, PlainSightDbContext context, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("VncProxy");

            if (!httpContext.WebSockets.IsWebSocketRequest)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            Device? device = await context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device is null || string.IsNullOrEmpty(device.CallbackUrl))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!Uri.TryCreate(device.CallbackUrl, UriKind.Absolute, out Uri? callbackUri))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            string host = callbackUri.Host;
            string? subProtocol = httpContext.WebSockets.WebSocketRequestedProtocols.Contains("binary") ? "binary" : null;

            using TcpClient tcp = new();
            try
            {
                await tcp.ConnectAsync(host, VncPort, ct);
            }
            catch (SocketException ex)
            {
                logger.LogWarning(ex, "VNC proxy could not reach {Host}:{Port} for device {DeviceId}", host, VncPort, deviceId);
                httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            using WebSocket socket = subProtocol is null
                ? await httpContext.WebSockets.AcceptWebSocketAsync()
                : await httpContext.WebSockets.AcceptWebSocketAsync(subProtocol);

            logger.LogInformation("VNC proxy connected admin to {Host}:{Port} for device {DeviceId}", host, VncPort, deviceId);

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            NetworkStream tcpStream = tcp.GetStream();

            Task browserToPlayer = PumpBrowserToPlayerAsync(socket, tcpStream, linkedCts);
            Task playerToBrowser = PumpPlayerToBrowserAsync(tcpStream, socket, linkedCts);

            await Task.WhenAny(browserToPlayer, playerToBrowser);
            await linkedCts.CancelAsync();
            await Task.WhenAll(
                browserToPlayer.ContinueWith(static _ => { }, TaskScheduler.Default),
                playerToBrowser.ContinueWith(static _ => { }, TaskScheduler.Default));

            logger.LogInformation("VNC proxy closed for device {DeviceId}", deviceId);
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });
    }

    private static async Task PumpBrowserToPlayerAsync(WebSocket socket, NetworkStream tcpStream, CancellationTokenSource linkedCts)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, linkedCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                await tcpStream.WriteAsync(buffer.AsMemory(0, result.Count), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // expected when the other direction ends the session
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // peer dropped the connection; the linked token tears down the other pump
        }
        finally
        {
            await linkedCts.CancelAsync();
        }
    }

    private static async Task PumpPlayerToBrowserAsync(NetworkStream tcpStream, WebSocket socket, CancellationTokenSource linkedCts)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                int read = await tcpStream.ReadAsync(buffer, linkedCts.Token);
                if (read == 0)
                {
                    break;
                }
                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // expected when the other direction ends the session
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // peer dropped the connection; the linked token tears down the other pump
        }
        finally
        {
            await linkedCts.CancelAsync();
        }
    }
}
