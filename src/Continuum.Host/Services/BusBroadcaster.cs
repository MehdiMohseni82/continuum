using Continuum.Core.Contracts;

namespace Continuum.Host.Services;

/// <summary>
/// In-process pub/sub that pushes bus activity to any live Blazor Server component (which rides
/// Blazor's own SignalR circuit). Single-host, so an in-memory aggregator is simpler and more
/// reliable than a separate hub, while still being genuinely live.
/// </summary>
public sealed class BusBroadcaster
{
    public event Action<MessageDto>? MessagePosted;
    public event Action<AgentDto>? AgentRegistered;
    public event Action<HandoffDto>? HandoffChanged;

    public void PublishMessage(MessageDto m) => MessagePosted?.Invoke(m);
    public void PublishAgent(AgentDto a) => AgentRegistered?.Invoke(a);
    public void PublishHandoff(HandoffDto h) => HandoffChanged?.Invoke(h);
}
