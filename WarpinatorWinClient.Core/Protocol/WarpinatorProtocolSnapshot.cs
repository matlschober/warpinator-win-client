namespace WarpinatorWinClient.Core.Protocol;

public sealed class WarpinatorProtocolSnapshot
{
    public required IReadOnlyList<int> SupportedApiVersions { get; init; }

    public required int DefaultTransferPort { get; init; }

    public required int DefaultAuthenticationPort { get; init; }

    public required IReadOnlyList<string> TransferServiceMethods { get; init; }

    public required IReadOnlyList<string> RegistrationServiceMethods { get; init; }

    public static WarpinatorProtocolSnapshot CreateDefault()
    {
        return new WarpinatorProtocolSnapshot
        {
            SupportedApiVersions = WarpinatorProtocolDefaults.SupportedApiVersions,
            DefaultTransferPort = WarpinatorProtocolDefaults.TransferPort,
            DefaultAuthenticationPort = WarpinatorProtocolDefaults.AuthenticationPort,
            TransferServiceMethods = WarpinatorProtocolDefaults.TransferServiceMethods,
            RegistrationServiceMethods = WarpinatorProtocolDefaults.RegistrationServiceMethods
        };
    }
}
