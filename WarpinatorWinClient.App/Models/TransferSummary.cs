namespace WarpinatorWinClient.App.Models;

public sealed class TransferSummary
{
    public required string Name { get; init; }

    public required string Peer { get; init; }

    public required string Direction { get; init; }

    public required string Status { get; init; }

    public required string Summary { get; init; }

    public required string ProgressSummary { get; init; }

    public required string SpeedSummary { get; init; }

    public required string EtaSummary { get; init; }

    public required double ProgressPercent { get; init; }

    public required bool IsActive { get; init; }

    public string? RevealPath { get; init; }

    public bool CanReveal { get; init; }
}
