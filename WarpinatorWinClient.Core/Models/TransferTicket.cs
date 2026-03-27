namespace WarpinatorWinClient.Core.Models;

public sealed class TransferTicket
{
    public required string Id { get; init; }

    public required string PeerId { get; init; }

    public required TransferDirection Direction { get; init; }

    public required string Summary { get; init; }
}
