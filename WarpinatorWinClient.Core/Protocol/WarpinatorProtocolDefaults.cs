namespace WarpinatorWinClient.Core.Protocol;

public static class WarpinatorProtocolDefaults
{
    public const int TransferPort = 42000;

    public const int AuthenticationPort = 42001;

    public const string DefaultGroupCode = "Warpinator";

    public static IReadOnlyList<int> SupportedApiVersions { get; } = new[] { 1, 2 };

    public static IReadOnlyList<string> TransferServiceMethods { get; } = new[]
    {
        "CheckDuplexConnection",
        "WaitingForDuplex",
        "GetRemoteMachineInfo",
        "GetRemoteMachineAvatar",
        "ProcessTransferOpRequest",
        "PauseTransferOp",
        "SendTextMessage",
        "StartTransfer",
        "CancelTransferOpRequest",
        "StopTransfer",
        "Ping"
    };

    public static IReadOnlyList<string> RegistrationServiceMethods { get; } = new[]
    {
        "RequestCertificate",
        "RegisterService"
    };
}
