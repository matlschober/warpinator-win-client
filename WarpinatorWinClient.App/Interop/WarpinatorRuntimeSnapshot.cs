using System;
using System.Collections.Generic;

namespace WarpinatorWinClient.App.Interop;

public sealed class WarpinatorRuntimeSnapshot
{
    public long Revision { get; init; }

    public bool IsRunning { get; init; }

    public bool IsSearching { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public string CurrentPhase { get; init; } = string.Empty;

    public string TransferReadiness { get; init; } = string.Empty;

    public string NextStep { get; init; } = string.Empty;

    public string PeerCountSummary { get; init; } = string.Empty;

    public string TransferCountSummary { get; init; } = string.Empty;

    public string DependencyBudgetSummary { get; init; } = string.Empty;

    public string ProtocolPortsSummary { get; init; } = string.Empty;

    public string ApiVersionsSummary { get; init; } = string.Empty;

    public string GroupCodeSummary { get; init; } = string.Empty;

    public string RpcSurfaceSummary { get; init; } = string.Empty;

    public string SaveFolderSummary { get; init; } = string.Empty;

    public IReadOnlyList<WarpinatorRuntimePeer> Peers { get; init; } = Array.Empty<WarpinatorRuntimePeer>();

    public IReadOnlyList<WarpinatorRuntimeTransfer> Transfers { get; init; } = Array.Empty<WarpinatorRuntimeTransfer>();
}
