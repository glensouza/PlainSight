using Microsoft.AspNetCore.SignalR;

namespace PlainSight.Server.Services;

public class DeviceHub : Hub
{
    // We can add methods here if the client needs to send data, 
    // but for now we only need it for broadcasting from the server.
}
