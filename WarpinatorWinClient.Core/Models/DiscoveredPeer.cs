namespace WarpinatorWinClient.Core.Models;

public sealed class DiscoveredPeer
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Hostname { get; init; }

    public required string Address { get; init; }

    public required int TransferPort { get; init; }

    public required int AuthenticationPort { get; init; }

    public required int ApiVersion { get; init; }

    public PeerAvailability Availability { get; init; } = PeerAvailability.Unknown;
}
