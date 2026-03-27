namespace WarpinatorWinClient.App.Interop;

public sealed class WarpinatorRuntimePeer
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Endpoint { get; init; }

    public required string Details { get; init; }

    public required string Availability { get; init; }
}
