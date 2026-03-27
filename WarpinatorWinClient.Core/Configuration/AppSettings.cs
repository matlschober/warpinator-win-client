namespace WarpinatorWinClient.Core.Configuration;

public sealed class AppSettings
{
    public string DisplayName { get; init; } = Environment.MachineName;

    public string GroupCode { get; init; } = Protocol.WarpinatorProtocolDefaults.DefaultGroupCode;

    public string SaveFolderPath { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public bool EnableAutomaticDiscovery { get; init; } = true;

    public bool EnableManualPeerEntry { get; init; } = true;

    public int TransferPort { get; init; } = Protocol.WarpinatorProtocolDefaults.TransferPort;

    public int AuthenticationPort { get; init; } = Protocol.WarpinatorProtocolDefaults.AuthenticationPort;
}
