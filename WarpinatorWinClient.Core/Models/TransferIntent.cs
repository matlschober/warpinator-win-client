namespace WarpinatorWinClient.Core.Models;

public sealed class TransferIntent
{
    public required string PeerId { get; init; }

    public required TransferDirection Direction { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public bool UseCompression { get; init; } = true;
}
