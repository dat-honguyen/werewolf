using Wolverine;

namespace Application.Werewolf.Notifications;

/// <summary>
/// Marker for messages that should be routed outbound to SignalR groups. Registered against
/// Wolverine's publishing rules (see CritterConfiguration.AddWolverinePlugin) so that
/// implementers — like PlayerNotification — actually reach the SignalR transport; UseSignalR()
/// alone only registers the transport, it does not subscribe any message type to it.
/// </summary>
public interface IGroupWebsocketMessage : WebSocketMessage;
