namespace WarpinatorWinClient.App.Models;

public sealed class PeerSummary
{
    public required string DisplayName { get; init; }

    public required string Endpoint { get; init; }

    public required string Details { get; init; }

    public required string Availability { get; init; }
}
