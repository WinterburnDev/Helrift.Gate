// Services/IChatBroadcaster.cs
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Services;

public interface IChatBroadcaster
{
    Task BroadcastAsync(ChatBroadcastData data, CancellationToken ct);
}
