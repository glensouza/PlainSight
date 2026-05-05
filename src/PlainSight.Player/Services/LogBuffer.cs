using System.Threading.Channels;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

internal sealed class LogBuffer
{
    private readonly Channel<DeviceLogEntryDto> channel = Channel.CreateBounded<DeviceLogEntryDto>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Add(DeviceLogEntryDto entry) => this.channel.Writer.TryWrite(entry);

    public List<DeviceLogEntryDto> DrainAll()
    {
        List<DeviceLogEntryDto> result = [];
        while (this.channel.Reader.TryRead(out DeviceLogEntryDto? entry))
        {
            result.Add(entry);
        }

        return result;
    }
}
