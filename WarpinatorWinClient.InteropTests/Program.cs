using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using WarpinatorWinClient.App.Interop;
using WarpinatorWinClient.Core.Configuration;

// Dependency-free integration runner. Real TLS/gRPC channels exercise registration,
// request callbacks, protobuf operation identities, and writes to an isolated folder.
// Private discovery entry points are invoked to avoid depending on LAN multicast.
var saveFolder = Path.Combine(Path.GetTempPath(), "warpinator-interop-" + Guid.NewGuid().ToString("N"));
var settings = new AppSettings { SaveFolderPath = saveFolder };
var profile = LocalMachineProfile.Create(settings);
var certificates = new WarpCertificateManager(profile);
var credentials = new SslServerCredentials(new[]
{
    new KeyCertificatePair(certificates.PublicCertificatePem, certificates.PrivateKeyPem)
});
using var runtime = new WarpinatorRuntime(settings);
SetField("localProfile", profile);
SetField("certificateManager", certificates);
var remote = new SimulatedLinuxPeer();
var registration = new SimulatedRegistration(certificates, settings.GroupCode, remote);
var remoteServer = new Server
{
    Services = { Warp.BindService(remote) },
    Ports = { new ServerPort(profile.Ipv4Address.ToString(), 0, credentials) }
};
var authServer = new Server
{
    Services = { WarpRegistration.BindService(registration) },
    Ports = { new ServerPort(profile.Ipv4Address.ToString(), 0, ServerCredentials.Insecure) }
};
var windowsServer = new Server
{
    Services = { Warp.BindService(new WarpServiceCompatImpl(runtime, profile)) },
    Ports = { new ServerPort(profile.Ipv4Address.ToString(), 0, credentials) }
};
var results = new ConcurrentDictionary<string, TaskCompletionSource<WarpinatorRuntimeTransfer>>();
var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
runtime.SnapshotChanged += (_, snapshot) =>
{
    if (snapshot.Peers.Any(peer => peer.Availability == "Connected")) connected.TrySetResult(true);
    foreach (var transfer in snapshot.Transfers)
    {
        if (transfer.Status is "Completed" or "Failed" && results.TryGetValue(transfer.Name, out var result))
            result.TrySetResult(transfer);
    }
};
Channel? windowsChannel = null;
try
{
    remoteServer.Start();
    authServer.Start();
    windowsServer.Start();
    Invoke("HandleRemoteRegistrationRequest", new ServiceRegistration
    {
        ServiceId = remote.Id,
        Hostname = "simulated-linux",
        Ip = profile.Ipv4Address.ToString(),
        Port = (uint)remoteServer.Ports.Single().BoundPort,
        AuthPort = (uint)authServer.Ports.Single().BoundPort,
        ApiVersion = 2
    });
    await (Task)Invoke("RegisterPeerAsync", remote.Id)!;
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Require(registration.CertificateRequests == 1, "The discovered peer's certificate was not requested.");
    Require(registration.ManualRegistrations == 0, "Discovery invoked manual registration, risking duplicate Linux peer records.");
    Console.WriteLine("PASS: discovered peer connects using certificate exchange without manual registration.");

    windowsChannel = new Channel($"{profile.Ipv4Address}:{windowsServer.Ports.Single().BoundPort}",
        new SslCredentials(certificates.PublicCertificatePem));
    var windows = new Warp.WarpClient(windowsChannel);
    var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 123);
    await Receive("single-file.bin", 66781777514, payload, payload.Length, "Completed");
    Require(SHA256.HashData(File.ReadAllBytes(Path.Combine(saveFolder, "single-file.bin")))
        .SequenceEqual(SHA256.HashData(payload)), "Received file content differs from the sender.");
    Require(!remote.CompressionRequested, "Windows requested compressed chunks without a decompressor.");
    Console.WriteLine("PASS: Linux-style offer preserves receiver ID and 64-bit timestamp, declines compression, and saves matching file bytes.");

    await Receive("truncated.bin", 66781777515, payload[..100], payload.Length, "Failed");
    await Receive("missing-operation.bin", 66781777516, Array.Empty<byte>(), payload.Length, "Failed");
    Console.WriteLine("PASS: truncated and empty non-empty-file streams fail instead of reporting success.");
    await Receive("empty-file.bin", 66781777517, Array.Empty<byte>(), 0, "Completed");
    Require(File.Exists(Path.Combine(saveFolder, "empty-file.bin")), "An empty file was not created.");
    Console.WriteLine("PASS: a valid zero-byte file still completes.");

    async Task Receive(string name, ulong timestamp, byte[] bytes, int offeredSize, string expectedStatus)
    {
        var completion = new TaskCompletionSource<WarpinatorRuntimeTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
        results[name] = completion;
        remote.Offers[timestamp] = new Offer(profile.ServiceId, name, bytes);
        await windows.ProcessTransferOpRequestAsync(new TransferOpRequest
        {
            Info = new OpInfo { Ident = remote.Id, ReadableName = "simulated-linux", Timestamp = timestamp, UseCompression = true },
            Receiver = profile.ServiceId,
            NameIfSingle = name,
            Size = (ulong)offeredSize,
            Count = 1,
            TopDirBasenames = { name }
        }, deadline: DateTime.UtcNow.AddSeconds(10));
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Require(result.Status == expectedStatus, $"{name}: expected {expectedStatus}, got {result.Status}: {result.Summary}");
    }
}
finally
{
    await runtime.StopAsync();
    if (windowsChannel is not null) await windowsChannel.ShutdownAsync();
    await windowsServer.ShutdownAsync();
    await remoteServer.ShutdownAsync();
    await authServer.ShutdownAsync();
    // Remove only the files this runner created; avoid recursive filesystem cleanup.
    if (Directory.Exists(saveFolder))
    {
        foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
        Directory.Delete(saveFolder);
    }
}

void SetField(string name, object value) => typeof(WarpinatorRuntime)
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, value);
object? Invoke(string name, object argument) => typeof(WarpinatorRuntime)
    .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, new[] { argument });
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record Offer(string Receiver, string Name, byte[] Bytes);

sealed class SimulatedLinuxPeer : Warp.WarpBase
{
    public string Id { get; } = "linux-interop-test";
    public ConcurrentDictionary<ulong, Offer> Offers { get; } = new();
    public bool CompressionRequested { get; private set; }
    public override Task<HaveDuplex> WaitingForDuplex(LookupName request, ServerCallContext context) => Task.FromResult(new HaveDuplex { Response = true });
    public override Task<HaveDuplex> CheckDuplexConnection(LookupName request, ServerCallContext context) => Task.FromResult(new HaveDuplex { Response = true });
    public override Task<RemoteMachineInfo> GetRemoteMachineInfo(LookupName request, ServerCallContext context) => Task.FromResult(new RemoteMachineInfo { DisplayName = "simulated-linux" });
    public override Task<VoidType> Ping(LookupName request, ServerCallContext context) => Task.FromResult(new VoidType());
    public override async Task StartTransfer(OpInfo request, IServerStreamWriter<FileChunk> stream, ServerCallContext context)
    {
        if (!Offers.TryGetValue(request.Timestamp, out var offer) || request.Ident != offer.Receiver)
            throw new RpcException(new Status(StatusCode.Unknown, "'NoneType' object has no attribute 'file_send_cancellable'"));
        CompressionRequested |= request.UseCompression;
        // Reproduce Linux's empty stream response when it has no usable operation.
        if (offer.Name == "missing-operation.bin") return;
        for (var offset = 0; offset < Math.Max(offer.Bytes.Length, 1); offset += 128 * 1024)
        {
            await stream.WriteAsync(new FileChunk
            {
                RelativePath = offer.Name,
                FileType = 1,
                Chunk = ByteString.CopyFrom(offer.Bytes, offset, Math.Min(128 * 1024, offer.Bytes.Length - offset))
            });
        }
    }
}

sealed class SimulatedRegistration : WarpRegistration.WarpRegistrationBase
{
    private readonly WarpCertificateManager certificates;
    private readonly string groupCode;
    private readonly SimulatedLinuxPeer remote;

    public SimulatedRegistration(WarpCertificateManager certificates, string groupCode, SimulatedLinuxPeer remote)
    {
        this.certificates = certificates;
        this.groupCode = groupCode;
        this.remote = remote;
    }

    public int CertificateRequests { get; private set; }
    public int ManualRegistrations { get; private set; }
    public override Task<RegResponse> RequestCertificate(RegRequest request, ServerCallContext context)
    {
        CertificateRequests++;
        return Task.FromResult(new RegResponse { LockedCert = certificates.GetEncodedLocalCertificate(groupCode) });
    }
    public override Task<ServiceRegistration> RegisterService(ServiceRegistration request, ServerCallContext context)
    {
        ManualRegistrations++;
        // A second registration may replace the upstream peer record and its operations.
        remote.Offers.Clear();
        return Task.FromResult(new ServiceRegistration { ServiceId = remote.Id, Hostname = "simulated-linux" });
    }
}
