using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Makaretu.Dns;
using WarpinatorWinClient.App.Interop;
using WarpinatorWinClient.Core.Configuration;

if (args.Contains("--discover"))
{
    await RunLiveDiscoveryAsync();
    return;
}

// Dependency-free integration runner. Real TLS/gRPC channels exercise registration,
// request callbacks, protobuf operation identities, and writes to an isolated folder.
// Private discovery entry points are invoked to avoid depending on LAN multicast.
var saveFolder = Path.Combine(Path.GetTempPath(), "warpinator-interop-" + Guid.NewGuid().ToString("N"));
var settings = new AppSettings { SaveFolderPath = saveFolder, RequireReceiveApproval = false };
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
WarpinatorRuntimeSnapshot? latestSnapshot = null;
runtime.SnapshotChanged += (_, snapshot) =>
{
    latestSnapshot = snapshot;
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
    Invoke("ObservePeerProbe", remote.Id, "simulated-linux", $"ipv4:{profile.Ipv4Address}:12345");
    Require(latestSnapshot?.Peers.Any(peer => peer.Id == remote.Id && peer.Availability == "Handshake") == true,
        "An inbound peer probe was not immediately published to the UI.");
    Console.WriteLine("PASS: an inbound peer appears immediately while discovery is still settling.");
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

    var discoveryMessage = new Message { QR = true };
    var instanceName = remote.Id + "._warpinator._tcp.local";
    discoveryMessage.Answers.Add(new SRVRecord
    {
        Name = instanceName, Target = "simulated-linux.local", Port = (ushort)remoteServer.Ports.Single().BoundPort
    });
    discoveryMessage.AdditionalRecords.Add(new TXTRecord
    {
        Name = instanceName,
        Strings = { "hostname=simulated-linux", "api-version=2", $"auth-port={authServer.Ports.Single().BoundPort}", "type=real" }
    });
    discoveryMessage.AdditionalRecords.Add(new ARecord { Name = "simulated-linux.local", Address = profile.Ipv4Address });
    for (var repeat = 0; repeat < 3; repeat++)
    {
        Invoke("OnServiceInstanceDiscovered", null, new ServiceInstanceDiscoveryEventArgs
        {
            ServiceInstanceName = instanceName, Message = discoveryMessage
        });
        await (Task)Invoke("RegisterPeerAsync", remote.Id)!;
    }
    Require(registration.CertificateRequests == 1, "Repeated discovery re-registered an already connected peer.");
    Require(latestSnapshot?.Peers.Single(peer => peer.Id == remote.Id).Availability == "Connected",
        "Repeated discovery downgraded a connected peer.");
    Console.WriteLine("PASS: repeated searches preserve connected peers without repeating certificate exchange.");

    Invoke("OnServiceInstanceShutdown", null, new ServiceInstanceShutdownEventArgs
    {
        ServiceInstanceName = profile.ServiceId + "._warpinator._tcp.local", Message = new Message()
    });
    Invoke("OnServiceInstanceShutdown", null, new ServiceInstanceShutdownEventArgs
    {
        ServiceInstanceName = remote.Id + "._unrelated._tcp.local", Message = new Message()
    });
    Require(latestSnapshot?.Peers.Single(peer => peer.Id == remote.Id).Availability == "Connected",
        "A local or unrelated goodbye changed the connected peer.");
    Console.WriteLine("PASS: local and unrelated mDNS goodbyes do not change the peer list.");

    windowsChannel = new Channel($"{profile.Ipv4Address}:{windowsServer.Ports.Single().BoundPort}",
        new SslCredentials(certificates.PublicCertificatePem));
    var windows = new Warp.WarpClient(windowsChannel);
    var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 123);

    Require(new AppSettings().RequireReceiveApproval, "Receive approval must be enabled by default.");
    var storePath = Path.Combine(saveFolder, "settings.json");
    var store = new JsonAppSettingsStore(storePath);
    Require((await store.LoadAsync(CancellationToken.None)).RequireReceiveApproval, "Missing settings must require approval.");
    await store.SaveAsync(settings, CancellationToken.None);
    var reloaded = await new JsonAppSettingsStore(storePath).LoadAsync(CancellationToken.None);
    Require(!reloaded.RequireReceiveApproval && reloaded.SaveFolderPath == saveFolder, "Receive preference was not persisted.");
    await store.SaveAsync(settings with { RequireReceiveApproval = true }, CancellationToken.None);
    Require((await store.LoadAsync(CancellationToken.None)).RequireReceiveApproval, "Approval could not be re-enabled persistently.");
    File.WriteAllText(storePath, "{}");
    Require((await store.LoadAsync(CancellationToken.None)).RequireReceiveApproval, "Older settings must require approval.");
    File.WriteAllText(storePath, "{invalid json}");
    try { await store.LoadAsync(CancellationToken.None); throw new InvalidOperationException("Malformed settings were accepted."); }
    catch (JsonException) { }
    Console.WriteLine("PASS: approval defaults on, persists both settings, and invalid settings never enable automatic receiving.");

    runtime.SetReceiveApprovalRequired(true);
    var pendingCompletion = new TaskCompletionSource<WarpinatorRuntimeTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
    results["approval.bin"] = pendingCompletion;
    var originalPath = Path.Combine(saveFolder, "approval.bin");
    File.WriteAllText(originalPath, "existing content");
    await OfferIncoming("approval.bin", 66781777600, payload, payload.Length);
    await OfferIncoming("approval.bin", 66781777600, payload, payload.Length);
    await Task.Delay(150);
    var pending = latestSnapshot!.Transfers.Single(transfer => transfer.Name == "approval.bin");
    Require(pending.RequiresApproval && !pending.IsActive, "The offer did not wait for approval.");
    Require(!remote.StartRequests.ContainsKey(66781777600), "A pending offer opened a stream.");
    Require(File.ReadAllText(originalPath) == "existing content", "A pending offer modified an existing file.");
    Require(runtime.ApproveIncomingTransfer(pending.Id), "The receive request could not be accepted.");
    Require(!runtime.ApproveIncomingTransfer(pending.Id), "A receive request could be approved twice.");
    Require((await pendingCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15))).Status == "Completed", "Approved receive did not complete.");
    Require(File.ReadAllBytes(originalPath).SequenceEqual(payload), "Approved receive changed the file bytes.");
    Console.WriteLine("PASS: pending and duplicate offers perform no I/O; acceptance starts one stream and saves matching bytes.");

    await OfferIncoming("declined.bin", 66781777601, payload, payload.Length);
    var declined = latestSnapshot!.Transfers.Single(transfer => transfer.Name == "declined.bin");
    await runtime.DeclineIncomingTransferAsync(declined.Id);
    Require(remote.Cancellations.TryGetValue(66781777601, out var cancelIdent) && cancelIdent == profile.ServiceId,
        "Decline did not notify the sender with the receiver ID and original timestamp.");
    Require(!File.Exists(Path.Combine(saveFolder, "declined.bin")) && !remote.StartRequests.ContainsKey(66781777601),
        "Declining an offer received file data.");
    Require(!runtime.ApproveIncomingTransfer(declined.Id), "A declined offer could still be approved.");
    Console.WriteLine("PASS: declining receives no bytes, creates no file, and sends Warpinator cancellation to the sender.");
    await OfferIncoming("declined.bin", 66781777601, payload, payload.Length);
    Require(latestSnapshot!.Transfers.Single(transfer => transfer.Name == "declined.bin").RequiresApproval,
        "A sender retry of a declined operation did not request fresh approval.");
    await runtime.DeclineIncomingTransferAsync(declined.Id);
    Console.WriteLine("PASS: retrying a declined operation asks for approval again.");

    await OfferIncoming("withdrawn.bin", 66781777602, payload, payload.Length);
    var withdrawn = latestSnapshot!.Transfers.Single(transfer => transfer.Name == "withdrawn.bin");
    await windows.CancelTransferOpRequestAsync(new OpInfo { Ident = remote.Id, Timestamp = 66781777602 });
    Require(!runtime.ApproveIncomingTransfer(withdrawn.Id) && !File.Exists(Path.Combine(saveFolder, "withdrawn.bin")),
        "A sender-cancelled offer could still be accepted.");
    Console.WriteLine("PASS: sender cancellation withdraws a pending offer and prevents later approval.");

    await OfferIncoming("still-pending.bin", 66781777603, payload, payload.Length);
    var stillPending = latestSnapshot!.Transfers.Single(transfer => transfer.Name == "still-pending.bin");
    runtime.SetReceiveApprovalRequired(false);
    Require(!remote.StartRequests.ContainsKey(66781777603), "Disabling approval accepted an already pending offer.");
    await runtime.DeclineIncomingTransferAsync(stillPending.Id);
    Console.WriteLine("PASS: changing the preference applies to new requests and does not silently accept pending offers.");

    runtime.SetReceiveApprovalRequired(true);
    EventHandler<WarpinatorRuntimeSnapshot> approveImmediately = (_, snapshot) =>
    {
        foreach (var transfer in snapshot.Transfers.Where(transfer => transfer.Name == "fast-approval.bin" && transfer.RequiresApproval))
            runtime.ApproveIncomingTransfer(transfer.Id);
    };
    runtime.SnapshotChanged += approveImmediately;
    try { await Receive("fast-approval.bin", 66781777604, payload, payload.Length, "Completed"); }
    finally { runtime.SnapshotChanged -= approveImmediately; }
    Require(remote.StartRequests[66781777604] == 1, "An immediate approval opened more than one stream.");
    runtime.SetReceiveApprovalRequired(false);
    Console.WriteLine("PASS: approval during offer notification starts exactly one receive stream.");

    await Receive("single-file.bin", 66781777514, payload, payload.Length, "Completed");
    Require(SHA256.HashData(File.ReadAllBytes(Path.Combine(saveFolder, "single-file.bin")))
        .SequenceEqual(SHA256.HashData(payload)), "Received file content differs from the sender.");
    Require(!remote.CompressionRequested, "Windows requested compressed chunks without a decompressor.");
    Console.WriteLine("PASS: Linux-style offer preserves receiver ID and 64-bit timestamp, declines compression, and saves matching file bytes.");

    await Receive("truncated.bin", 66781777515, payload[..100], payload.Length, "Failed");
    await Receive("missing-operation.bin", 66781777516, Array.Empty<byte>(), payload.Length, "Failed");
    Console.WriteLine("PASS: a gRPC stream error after partial data and an empty missing-operation stream fail.");
    await Receive("empty-file.bin", 66781777517, Array.Empty<byte>(), 0, "Completed");
    Require(File.Exists(Path.Combine(saveFolder, "empty-file.bin")), "An empty file was not created.");
    Console.WriteLine("PASS: a valid zero-byte file still completes.");

    var allocatedPayload = RandomNumberGenerator.GetBytes(1295651);
    await Receive("allocated-size.bin", 66781777518, allocatedPayload, 1298432, "Completed");
    Require(SHA256.HashData(File.ReadAllBytes(Path.Combine(saveFolder, "allocated-size.bin")))
        .SequenceEqual(SHA256.HashData(allocatedPayload)), "Allocated-size transfer changed file content.");
    var allocatedResult = await results["allocated-size.bin"].Task;
    Require(allocatedResult.ProgressPercent == 100, "Completed allocation-sized transfer did not reach 100 percent.");
    Require(allocatedResult.ProgressSummary.Split(" / ") is [var receivedSize, var totalSize] && receivedSize == totalSize,
        "Completed transfer still displays the advertised allocation instead of actual bytes.");
    Console.WriteLine("PASS: the reported FLAC size mismatch (1295651 payload / 1298432 advertised) completes with matching bytes and 100 percent progress.");

    await Receive("one-byte.bin", 66781777519, new byte[] { 42 }, 4096, "Completed");
    await Receive("allocated-empty-file.bin", 66781777520, Array.Empty<byte>(), 4096, "Completed");
    await Receive("empty-directory", 66781777521, Array.Empty<byte>(), 4096, "Completed", fileType: 2);
    Require(Directory.Exists(Path.Combine(saveFolder, "empty-directory")), "An empty directory was not created.");
    Console.WriteLine("PASS: allocation overhead is accepted for tiny files, zero-byte files, and empty directories.");

    await Receive("missing-item.bin", 66781777522, payload, payload.Length, "Failed", offeredCount: 2);
    var missingItemResult = await results["missing-item.bin"].Task;
    Require(missingItemResult.Summary.Contains("1 of 2 items"), "Missing-item failure did not report the item count.");
    Console.WriteLine("PASS: a clean stream that omits an advertised item still fails; multiple chunks count as one file.");

    Invoke("OnServiceInstanceShutdown", null, new ServiceInstanceShutdownEventArgs
    {
        ServiceInstanceName = instanceName, Message = new Message()
    });
    Require(latestSnapshot?.Peers.Single(peer => peer.Id == remote.Id).Availability == "Offline",
        "A peer goodbye was not shown as offline.");
    connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    for (var attempt = 0; attempt < 40 && !connected.Task.IsCompleted; attempt++)
    {
        Invoke("OnServiceInstanceDiscovered", null, new ServiceInstanceDiscoveryEventArgs
        {
            ServiceInstanceName = instanceName, Message = discoveryMessage
        });
        await Task.Delay(50);
    }
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Require(registration.CertificateRequests == 2, "The returning peer did not establish a fresh certificate exchange.");
    Console.WriteLine("PASS: a peer that goes offline reconnects when its service returns.");

    remote.RequireRegistrationRefresh = true;
    Invoke("OnServiceInstanceShutdown", null, new ServiceInstanceShutdownEventArgs
    {
        ServiceInstanceName = instanceName, Message = new Message()
    });
    connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    for (var attempt = 0; attempt < 100 && !connected.Task.IsCompleted; attempt++)
    {
        Invoke("OnServiceInstanceDiscovered", null, new ServiceInstanceDiscoveryEventArgs
        {
            ServiceInstanceName = instanceName, Message = discoveryMessage
        });
        await Task.Delay(50);
    }
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Require(registration.ManualRegistrations == 1, "The stale duplex connection was not recovered with one targeted registration refresh.");
    Require(remote.RegistrationRefreshed, "The peer's cached registration was not refreshed.");
    Console.WriteLine("PASS: a Linux-style stale-certificate duplex timeout triggers a targeted registration refresh and reconnects.");

    runtime.SetReceiveApprovalRequired(true);
    for (var index = 0; index < 10; index++)
        await OfferIncoming($"pending-{index}.bin", 66781777700 + (ulong)index, new byte[] { 42 }, 1);
    var queued = latestSnapshot!.Transfers.Where(transfer => transfer.RequiresApproval).ToArray();
    Require(queued.Length == 10, "Pending offers were hidden by the recent-history limit.");
    await runtime.StopAsync();
    foreach (var request in queued)
        Require(!runtime.ApproveIncomingTransfer(request.Id), "Stopping the service left an offer approvable.");
    Require(!Directory.GetFiles(saveFolder, "pending-*.bin").Any(), "A queued offer wrote files during shutdown.");
    Console.WriteLine("PASS: all pending requests remain visible beyond the history limit and shutdown cancels them without writing files.");

    async Task Receive(string name, ulong timestamp, byte[] bytes, int offeredSize, string expectedStatus,
        ulong offeredCount = 1, int fileType = 1)
    {
        var completion = new TaskCompletionSource<WarpinatorRuntimeTransfer>(TaskCreationOptions.RunContinuationsAsynchronously);
        results[name] = completion;
        await OfferIncoming(name, timestamp, bytes, offeredSize, offeredCount, fileType);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Require(result.Status == expectedStatus, $"{name}: expected {expectedStatus}, got {result.Status}: {result.Summary}");
        if (expectedStatus == "Completed")
            Require(result.ProgressPercent == 100, $"{name}: completed transfer progress is not 100 percent.");
    }

    async Task OfferIncoming(string name, ulong timestamp, byte[] bytes, int offeredSize, ulong offeredCount = 1, int fileType = 1)
    {
        remote.Offers[timestamp] = new Offer(profile.ServiceId, name, bytes, fileType);
        await windows.ProcessTransferOpRequestAsync(new TransferOpRequest
        {
            Info = new OpInfo { Ident = remote.Id, ReadableName = "simulated-linux", Timestamp = timestamp, UseCompression = true },
            Receiver = profile.ServiceId,
            NameIfSingle = name,
            Size = (ulong)offeredSize,
            Count = offeredCount,
            TopDirBasenames = { name }
        }, deadline: DateTime.UtcNow.AddSeconds(10));
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
        foreach (var directory in Directory.GetDirectories(saveFolder)) Directory.Delete(directory);
        Directory.Delete(saveFolder);
    }
}

void SetField(string name, object value) => typeof(WarpinatorRuntime)
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, value);
object? Invoke(string name, params object?[] arguments) => typeof(WarpinatorRuntime)
    .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, arguments);
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task RunLiveDiscoveryAsync()
{
    using var liveRuntime = new WarpinatorRuntime();
    var liveConnected = new TaskCompletionSource<WarpinatorRuntimePeer>(TaskCreationOptions.RunContinuationsAsynchronously);
    liveRuntime.SnapshotChanged += (_, snapshot) =>
    {
        Console.WriteLine(snapshot.StatusMessage);
        foreach (var peer in snapshot.Peers)
        {
            Console.WriteLine($"  {peer.DisplayName} | {peer.Endpoint} | {peer.Availability}");
            if (peer.Availability == "Connected" && peer.Details.StartsWith("Secure Warp session active", StringComparison.Ordinal))
                liveConnected.TrySetResult(peer);
        }
    };
    try
    {
        await liveRuntime.StartAsync(CancellationToken.None);
        var peer = await liveConnected.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await liveRuntime.SearchPeersAsync();
        Console.WriteLine($"PASS: live discovery and secure Warp connection with {peer.DisplayName} at {peer.Endpoint}; manual search completed.");
    }
    finally
    {
        await liveRuntime.StopAsync();
    }
}

sealed record Offer(string Receiver, string Name, byte[] Bytes, int FileType = 1);

sealed class SimulatedLinuxPeer : Warp.WarpBase
{
    public string Id { get; } = "linux-interop-test";
    public ConcurrentDictionary<ulong, Offer> Offers { get; } = new();
    public ConcurrentDictionary<ulong, int> StartRequests { get; } = new();
    public ConcurrentDictionary<ulong, string> Cancellations { get; } = new();

    public override Task<VoidType> CancelTransferOpRequest(OpInfo request, ServerCallContext context)
    {
        Cancellations[request.Timestamp] = request.Ident;
        return Task.FromResult(new VoidType());
    }
    public bool CompressionRequested { get; private set; }
    public bool RequireRegistrationRefresh { get; set; }
    public bool RegistrationRefreshed { get; set; }
    public override Task<HaveDuplex> WaitingForDuplex(LookupName request, ServerCallContext context)
    {
        if (RequireRegistrationRefresh && !RegistrationRefreshed)
            throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                "Server timed out while waiting for his corresponding remote to connect back to you."));
        return Task.FromResult(new HaveDuplex { Response = true });
    }
    public override Task<HaveDuplex> CheckDuplexConnection(LookupName request, ServerCallContext context) => Task.FromResult(new HaveDuplex { Response = true });
    public override Task<RemoteMachineInfo> GetRemoteMachineInfo(LookupName request, ServerCallContext context) => Task.FromResult(new RemoteMachineInfo { DisplayName = "simulated-linux" });
    public override Task<VoidType> Ping(LookupName request, ServerCallContext context) => Task.FromResult(new VoidType());
    public override async Task StartTransfer(OpInfo request, IServerStreamWriter<FileChunk> stream, ServerCallContext context)
    {
        StartRequests.AddOrUpdate(request.Timestamp, 1, (_, count) => count + 1);
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
                FileType = offer.FileType,
                Chunk = ByteString.CopyFrom(offer.Bytes, offset, Math.Min(128 * 1024, offer.Bytes.Length - offset))
            });
        }
        if (offer.Name == "truncated.bin")
            throw new RpcException(new Status(StatusCode.DataLoss, "Simulated sender stream failure after partial data."));
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
        remote.RegistrationRefreshed = true;
        // A second registration may replace the upstream peer record and its operations.
        remote.Offers.Clear();
        return Task.FromResult(new ServiceRegistration { ServiceId = remote.Id, Hostname = "simulated-linux" });
    }
}
