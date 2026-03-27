using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Makaretu.Dns;
using WarpinatorWinClient.Core.Configuration;
using WarpinatorWinClient.Core.Protocol;

namespace WarpinatorWinClient.App.Interop;

public sealed class WarpinatorRuntime : IDisposable
{
    private const string ServiceType = "_warpinator._tcp";

    private readonly object gate = new();
    private readonly Dictionary<string, RuntimePeerState> peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeTransferState> transfers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> peerSessionTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings settings = new();

    private LocalMachineProfile? localProfile;
    private WarpCertificateManager? certificateManager;
    private Server? registrationServer;
    private Server? transferServer;
    private MulticastService? mdns;
    private ServiceDiscovery? discovery;
    private ServiceProfile? advertisedProfile;
    private bool started;

    public event EventHandler<WarpinatorRuntimeSnapshot>? SnapshotChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (started)
        {
            RuntimeLog.Write("Start requested while runtime already active.");
            PublishSnapshot("LAN services already active.", "Discovery and registration are running.", "Observe peers or test against Linux Warpinator.");
            return;
        }

        RuntimeLog.Reset();
        RuntimeLog.Write("Runtime startup requested.");

        try
        {
            localProfile = LocalMachineProfile.Create(settings);
            RuntimeLog.Write($"Local profile ready: {localProfile.Ipv4Address}, transfer {localProfile.TransferPort}, auth {localProfile.AuthenticationPort}, service id {localProfile.ServiceId}");
            certificateManager = new WarpCertificateManager(localProfile);
            RuntimeLog.Write("Certificate manager created.");

            StartRegistrationServer();
            RuntimeLog.Write("Registration server started.");
            StartTransferServer();
            RuntimeLog.Write("Transfer compatibility server started.");
            StartMdns();
            RuntimeLog.Write("mDNS advertisement and browsing started.");

            started = true;
            PublishSnapshot(
                "LAN services are up. Browsing for Linux Warpinator peers.",
                "Discovery and registration live",
                "Wait for discovery or let your Linux machine see this Windows client on the LAN.");

            await Task.Delay(250, cancellationToken);
            discovery?.QueryServiceInstances(ServiceType);
            discovery?.QueryUnicastServiceInstances(ServiceType);
        }
        catch (Exception ex)
        {
            RuntimeLog.Write("Startup exception: " + ex);
            await StopAsync();
            PublishSnapshot(
                "Runtime startup failed: " + ex.Message,
                "Startup blocked",
                "Free ports 42000 and 42001 if another Warpinator-compatible app is already running, then restart this app.");
        }
    }

    public async Task StopAsync()
    {
        RuntimeLog.Write("Stopping runtime services.");

        List<CancellationTokenSource> sessionTokens;
        lock (gate)
        {
            sessionTokens = peerSessionTokens.Values.ToList();
            peerSessionTokens.Clear();
        }

        foreach (var sessionToken in sessionTokens)
        {
            sessionToken.Cancel();
            sessionToken.Dispose();
        }

        discovery?.Unadvertise();
        discovery?.Dispose();
        discovery = null;

        mdns?.Stop();
        mdns?.Dispose();
        mdns = null;

        if (transferServer is not null)
        {
            await transferServer.ShutdownAsync();
            transferServer = null;
        }

        if (registrationServer is not null)
        {
            await registrationServer.ShutdownAsync();
            registrationServer = null;
        }

        started = false;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async Task SendFileToConnectedPeerAsync(string filePath)
    {
        if (localProfile is null)
        {
            throw new InvalidOperationException("The runtime has not started yet.");
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        RuntimePeerState peerState;
        Warp.WarpClient client;
        lock (gate)
        {
            peerState = peers.Values
                .Where(peer => !string.IsNullOrWhiteSpace(peer.RemoteCertificatePem))
                .OrderByDescending(peer => peer.HasActiveSecureSession)
                .ThenBy(peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No connected Linux Warpinator peer is available yet.");

            client = peerState.ActiveSecureSessionClient
                ?? throw new InvalidOperationException("The Linux peer is discovered, but the maintained secure session is not ready yet.");
        }

        var fileInfo = new FileInfo(filePath);
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var transferKey = CreateTransferKey(peerState.Id, timestamp);

        lock (gate)
        {
            transfers[transferKey] = new RuntimeTransferState(transferKey)
            {
                PeerId = peerState.Id,
                PeerDisplayName = string.IsNullOrWhiteSpace(peerState.DisplayName) ? peerState.Id : peerState.DisplayName,
                Name = fileInfo.Name,
                LocalFilePath = fileInfo.FullName,
                OpTimestamp = timestamp,
                OperationIdent = localProfile.ServiceId,
                OperationReadableName = localProfile.HostName,
                UseCompression = false,
                TotalSize = (ulong)fileInfo.Length,
                TotalCount = 1,
                TopLevelNames = new[] { fileInfo.Name },
                MimeType = "application/octet-stream",
                Status = "Awaiting receiver",
                Summary = $"Offered to {peerState.DisplayName}. Waiting for Linux approval.",
                StartedAtUtc = DateTimeOffset.UtcNow
            };
        }

        RuntimeLog.Write($"Offering outbound transfer '{fileInfo.Name}' to {peerState.DisplayName}.");
        PublishSnapshot(
            $"Offered {fileInfo.Name} to {peerState.DisplayName}.",
            "Outbound transfer pending",
            "Approve the file on Linux so it can pull the stream from Windows.");

        try
        {
            await client.ProcessTransferOpRequestAsync(
                new TransferOpRequest
                {
                    Info = new OpInfo
                    {
                        Ident = localProfile.ServiceId,
                        Timestamp = timestamp,
                        ReadableName = localProfile.HostName,
                        UseCompression = false
                    },
                    SenderName = localProfile.DisplayName,
                    Receiver = peerState.Id,
                    Size = (ulong)fileInfo.Length,
                    Count = 1,
                    NameIfSingle = fileInfo.Name,
                    MimeIfSingle = "application/octet-stream",
                    TopDirBasenames = { fileInfo.Name }
                },
                deadline: DateTime.UtcNow.AddSeconds(10));
        }
        catch
        {
            lock (gate)
            {
                if (transfers.TryGetValue(transferKey, out var transferState))
                {
                    transferState.Status = "Failed";
                    transferState.CompletedAtUtc = DateTimeOffset.UtcNow;
                    transferState.Summary = "Could not deliver the outbound transfer request to Linux.";
                }
            }

            PublishSnapshot(
                "The outbound transfer request failed.",
                "Outbound transfer pending",
                "Check the peer connection and try again.");
            throw;
        }
        finally
        {
            PublishSnapshot(
                $"Waiting for Linux to accept {fileInfo.Name}.",
                "Outbound transfer pending",
                "The file offer is live on the maintained secure session.");
        }
    }

    internal bool IsKnownPeer(string serviceId)
    {
        lock (gate)
        {
            return peers.ContainsKey(serviceId);
        }
    }

    internal void ObservePeerProbe(string serviceId, string readableName, string remotePeerDescriptor)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return;
        }

        lock (gate)
        {
            if (!peers.TryGetValue(serviceId, out var peerState))
            {
                peerState = new RuntimePeerState(serviceId);
                peers[serviceId] = peerState;
            }

            if (string.IsNullOrWhiteSpace(peerState.DisplayName))
            {
                peerState.DisplayName = string.IsNullOrWhiteSpace(readableName) ? serviceId : readableName;
            }

            if (string.IsNullOrWhiteSpace(peerState.Hostname))
            {
                peerState.Hostname = peerState.DisplayName;
            }

            var parsedAddress = TryParseContextAddress(remotePeerDescriptor);
            if (!string.IsNullOrWhiteSpace(parsedAddress))
            {
                peerState.Address = parsedAddress;
            }

            if (string.IsNullOrWhiteSpace(peerState.Availability) || peerState.Availability is "Pending" or "Offline")
            {
                peerState.Availability = "Handshake";
            }

            if (string.IsNullOrWhiteSpace(peerState.Details))
            {
                peerState.Details = "Observed an inbound Warpinator probe before full peer discovery completed.";
            }
        }

        RuntimeLog.Write($"Observed inbound peer probe from '{serviceId}' ({readableName}) via {remotePeerDescriptor}.");
    }

    internal bool CanAcceptDuplexProbe(string serviceId, string readableName, string remotePeerDescriptor)
    {
        ObservePeerProbe(serviceId, readableName, remotePeerDescriptor);
        return !string.IsNullOrWhiteSpace(serviceId);
    }

    internal void AcceptIncomingTransferRequest(TransferOpRequest request)
    {
        var transferKey = CreateTransferKey(request.Info.Ident, request.Info.Timestamp);
        RuntimeTransferState transferState;
        string peerDisplayName;

        lock (gate)
        {
            if (!peers.TryGetValue(request.Info.Ident, out var peerState))
            {
                RuntimeLog.Write($"Transfer request ignored because peer '{request.Info.Ident}' is not known.");
                return;
            }

            if (!transfers.TryGetValue(transferKey, out transferState!))
            {
                transferState = new RuntimeTransferState(transferKey)
                {
                    PeerId = request.Info.Ident,
                    StartedAtUtc = DateTimeOffset.UtcNow
                };
                transfers[transferKey] = transferState;
            }

            peerDisplayName = string.IsNullOrWhiteSpace(peerState.DisplayName) ? request.Info.Ident : peerState.DisplayName;
            transferState.PeerId = request.Info.Ident;
            transferState.PeerDisplayName = peerDisplayName;
            transferState.OpTimestamp = request.Info.Timestamp;
            transferState.OperationIdent = request.Info.Ident;
            transferState.OperationReadableName = request.Info.ReadableName;
            transferState.UseCompression = request.Info.UseCompression;
            transferState.Name = string.IsNullOrWhiteSpace(request.NameIfSingle)
                ? request.TopDirBasenames.FirstOrDefault() ?? "Incoming transfer"
                : request.NameIfSingle;
            transferState.TotalSize = request.Size;
            transferState.TotalCount = request.Count;
            transferState.TopLevelNames = request.TopDirBasenames.ToArray();
            transferState.Status = "Awaiting stream";
            transferState.Summary = $"Accepted {transferState.Name} from {peerDisplayName}. Preparing secure receive stream.";

            if (transferState.IsStreamActive)
            {
                RuntimeLog.Write($"Transfer request for '{transferState.Name}' already being processed.");
                PublishSnapshot(
                    $"Transfer already active for {transferState.Name}.",
                    "Inbound transfer active",
                    "Watch the transfer history while the current receive completes.");
                return;
            }

            transferState.IsInbound = true;
            transferState.IsStreamActive = true;
        }

        RuntimeLog.Write($"Accepted incoming transfer request '{transferState.Name}' from {peerDisplayName}.");
        PublishSnapshot(
            $"Accepted incoming transfer from {peerDisplayName}.",
            "Inbound transfer active",
            "Receiving the file stream from Linux into the configured save folder.");

        _ = Task.Run(() => ReceiveTransferFromPeerAsync(transferKey));
    }

    internal void HandleRemoteTransferStop(StopInfo request)
    {
        var transferKey = CreateTransferKey(request.Info.Ident, request.Info.Timestamp);
        lock (gate)
        {
            if (!transfers.TryGetValue(transferKey, out var transferState))
            {
                return;
            }

            transferState.IsStreamActive = false;
            transferState.Status = request.Error ? "Stopped with error" : "Stopped remotely";
            transferState.Summary = request.Error
                ? "The remote sender reported an error while transferring this item."
                : "The remote sender stopped this transfer.";
        }

        RuntimeLog.Write($"Remote stop received for transfer {transferKey}. Error={request.Error}.");
        PublishSnapshot(
            "A transfer was stopped by the remote sender.",
                "Inbound transfer active",
                "Review the transfer history for the latest stop reason.");
    }

    internal void HandleRemoteTransferCancellation(OpInfo request)
    {
        var transferKey = CreateTransferKey(request.Ident, request.Timestamp);

        lock (gate)
        {
            if (!transfers.TryGetValue(transferKey, out var transferState))
            {
                return;
            }

            transferState.IsStreamActive = false;
            transferState.Status = "Cancelled";
            transferState.CompletedAtUtc = DateTimeOffset.UtcNow;
            transferState.Summary = "The Linux peer cancelled or declined this transfer.";
        }

        RuntimeLog.Write($"Remote cancellation received for transfer {transferKey}.");
        PublishSnapshot(
            "A transfer was declined or cancelled on Linux.",
            "Outbound transfer pending",
            "Choose another file or retry once the Linux side is ready.");
    }

    internal async Task StreamOutgoingTransferAsync(OpInfo request, IServerStreamWriter<FileChunk> responseStream)
    {
        var transferKey = CreateTransferKey(request.Ident, request.Timestamp);
        RuntimeTransferState transferState;

        lock (gate)
        {
            if (!transfers.TryGetValue(transferKey, out transferState!))
            {
                throw new RpcException(new Status(StatusCode.NotFound, "The requested outbound transfer was not found."));
            }

            if (string.IsNullOrWhiteSpace(transferState.LocalFilePath) || !File.Exists(transferState.LocalFilePath))
            {
                throw new RpcException(new Status(StatusCode.NotFound, "The source file for this transfer is no longer available."));
            }

            transferState.Status = "Sending";
            transferState.Summary = $"Linux approved {transferState.Name}. Streaming now.";
            transferState.ActivityStartedAtUtc ??= DateTimeOffset.UtcNow;
        }

        RuntimeLog.Write($"Outbound StartTransfer approved for {transferState.Name}.");
        PublishSnapshot(
            $"Streaming {transferState.Name} to Linux.",
            "Outbound transfer active",
            "Windows is serving file chunks to the Linux peer.");

        var reader = new OutgoingTransferReader(transferState.LocalFilePath!, transferState.Name);

        await foreach (var chunk in reader.ReadChunksAsync())
        {
            await responseStream.WriteAsync(chunk);

            var shouldPublishProgress = false;
            lock (gate)
            {
                transferState.BytesSent += (ulong)chunk.Chunk.Length;
                transferState.Status = "Sending";
                transferState.Summary = $"Sent {FormatSize(transferState.BytesSent)} of {FormatSize(transferState.TotalSize)} to {transferState.PeerDisplayName}.";
                shouldPublishProgress = ShouldPublishProgressSnapshot(transferState);
            }

            if (shouldPublishProgress)
            {
                PublishSnapshot(
                    $"Streaming {transferState.Name} to Linux.",
                    "Outbound transfer active",
                    "Windows is serving file chunks to the Linux peer.");
            }
        }

        lock (gate)
        {
            transferState.Status = "Completed";
            transferState.CompletedAtUtc = DateTimeOffset.UtcNow;
            transferState.Summary = $"Delivered from {transferState.LocalFilePath} to {transferState.PeerDisplayName}.";
        }

        RuntimeLog.Write($"Outbound transfer completed successfully: {transferState.Name}.");
        PublishSnapshot(
            $"Transfer completed: {transferState.Name}.",
            "Outbound transfer active",
            "The first Windows-to-Linux file flow completed.");
    }

    private void StartRegistrationServer()
    {
        var profile = localProfile ?? throw new InvalidOperationException("Local profile has not been initialized.");
        var certs = certificateManager ?? throw new InvalidOperationException("Certificates have not been initialized.");

        RuntimeLog.Write("Binding registration server.");
        registrationServer = new Server(CreateGrpcServerOptions())
        {
            Services =
            {
                WarpRegistration.BindService(new WarpRegistrationServiceImpl(profile, certs, HandleRemoteRegistrationRequest))
            },
            Ports =
            {
                new ServerPort(profile.Ipv4Address.ToString(), profile.AuthenticationPort, ServerCredentials.Insecure)
            }
        };
        registrationServer.Start();
    }

    private void StartTransferServer()
    {
        var profile = localProfile ?? throw new InvalidOperationException("Local profile has not been initialized.");
        var certs = certificateManager ?? throw new InvalidOperationException("Certificates have not been initialized.");
        var credentials = new SslServerCredentials(new[] { new KeyCertificatePair(certs.PublicCertificatePem, certs.PrivateKeyPem) });

        RuntimeLog.Write("Binding transfer server.");
        transferServer = new Server(CreateGrpcServerOptions())
        {
            Services =
            {
                Warp.BindService(new WarpServiceCompatImpl(this, profile))
            },
            Ports =
            {
                new ServerPort(profile.Ipv4Address.ToString(), profile.TransferPort, credentials)
            }
        };
        transferServer.Start();
    }

    private void StartMdns()
    {
        var profile = localProfile ?? throw new InvalidOperationException("Local profile has not been initialized.");

        RuntimeLog.Write("Starting mDNS stack.");
        mdns = new MulticastService();
        mdns.Start();

        discovery = new ServiceDiscovery(mdns);
        discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;

        advertisedProfile = new ServiceProfile(profile.ServiceId, ServiceType, (ushort)profile.TransferPort, new[] { profile.Ipv4Address })
        {
            HostName = profile.HostName + ".local"
        };
        advertisedProfile.AddProperty("hostname", profile.HostName);
        advertisedProfile.AddProperty("api-version", profile.ApiVersion.ToString(CultureInfo.InvariantCulture));
        advertisedProfile.AddProperty("auth-port", profile.AuthenticationPort.ToString(CultureInfo.InvariantCulture));
        advertisedProfile.AddProperty("type", "real");
        discovery.Advertise(advertisedProfile);
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        if (localProfile is null)
        {
            return;
        }

        RuntimeLog.Write("Service instance discovered event received: " + e.ServiceInstanceName);
        var discoveredPeer = TryParsePeer(e);
        if (discoveredPeer is null || string.Equals(discoveredPeer.Id, localProfile.ServiceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RuntimePeerState peerState;
        lock (gate)
        {
            if (!peers.TryGetValue(discoveredPeer.Id, out peerState!))
            {
                peerState = new RuntimePeerState(discoveredPeer.Id);
                peers[discoveredPeer.Id] = peerState;
            }

            peerState.DisplayName = discoveredPeer.DisplayName;
            peerState.Hostname = discoveredPeer.Hostname;
            peerState.Address = discoveredPeer.Address;
            peerState.TransferPort = discoveredPeer.TransferPort;
            peerState.AuthenticationPort = discoveredPeer.AuthenticationPort;
            peerState.ApiVersion = discoveredPeer.ApiVersion;
            peerState.Availability = "Discovered";
            peerState.Details = $"api v{peerState.ApiVersion}, transfer {peerState.TransferPort}, auth {peerState.AuthenticationPort}";
        }

        RuntimeLog.Write($"Discovered peer {discoveredPeer.DisplayName} at {discoveredPeer.Address}:{discoveredPeer.TransferPort} auth {discoveredPeer.AuthenticationPort} api {discoveredPeer.ApiVersion}.");
        PublishSnapshot(
            $"Discovered {discoveredPeer.DisplayName} on the LAN. Registering now.",
            "Discovery and registration live",
            "Certificate exchange is in progress.");

        _ = Task.Run(() => RegisterPeerAsync(discoveredPeer.Id));
    }

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var instanceName = e.ServiceInstanceName.ToString();
        var serviceId = instanceName.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];

        lock (gate)
        {
            if (peers.TryGetValue(serviceId, out var peerState))
            {
                peerState.Availability = "Offline";
                peerState.Details = "The peer stopped advertising its Warpinator service.";
            }
        }

        RuntimeLog.Write("Peer shutdown observed: " + serviceId);
        PublishSnapshot(
            "A discovered peer went offline.",
            "Discovery and registration live",
            "Waiting for peers to reappear.");
    }

    private async Task RegisterPeerAsync(string peerId)
    {
        if (localProfile is null || certificateManager is null)
        {
            return;
        }

        RuntimePeerState peerState;
        lock (gate)
        {
            if (!peers.TryGetValue(peerId, out peerState!))
            {
                return;
            }

            if (peerState.IsRegistering)
            {
                return;
            }

            peerState.IsRegistering = true;
            peerState.Availability = "Registering";
            peerState.Details = "Requesting remote certificate and announcing this Windows client.";
        }
        RuntimeLog.Write($"Attempting registration with {peerState.DisplayName} at {peerState.Address}:{peerState.AuthenticationPort}.");
        PublishSnapshot(
            $"Registering with {peerState.DisplayName}.",
            "Discovery and registration live",
            "Waiting for Warpinator auth response.");

        Channel? channel = null;

        try
        {
            channel = new Channel($"{peerState.Address}:{peerState.AuthenticationPort}", ChannelCredentials.Insecure);
            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));

            var registrationClient = new WarpRegistration.WarpRegistrationClient(channel);
            var certificateResponse = await registrationClient.RequestCertificateAsync(
                new RegRequest
                {
                    Ip = localProfile.Ipv4Address.ToString(),
                    Hostname = localProfile.HostName,
                    Ipv6 = string.Empty
                },
                deadline: DateTime.UtcNow.AddSeconds(5));

            if (!certificateManager.TryDecryptRemoteCertificate(certificateResponse.LockedCert, localProfile.GroupCode, out var remoteCertificatePem))
            {
                lock (gate)
                {
                    peerState.IsRegistering = false;
                    peerState.Availability = "Auth failed";
                    peerState.Details = "Received a certificate response, but it could not be decrypted with the current group code.";
                }

                RuntimeLog.Write($"Registration failed for {peerState.DisplayName}: certificate decrypt failed.");
                PublishSnapshot(
                    $"Registration failed for {peerState.DisplayName}.",
                    "Discovery and registration live",
                    "Check that the Windows app and Linux Warpinator use the same group code.");
                return;
            }

            peerState.RemoteCertificatePem = remoteCertificatePem;

            var serviceRegistration = await registrationClient.RegisterServiceAsync(
                new ServiceRegistration
                {
                    ServiceId = localProfile.ServiceId,
                    Ip = localProfile.Ipv4Address.ToString(),
                    Port = (uint)localProfile.TransferPort,
                    Hostname = localProfile.HostName,
                    ApiVersion = (uint)localProfile.ApiVersion,
                    AuthPort = (uint)localProfile.AuthenticationPort,
                    Ipv6 = string.Empty
                },
                deadline: DateTime.UtcNow.AddSeconds(5));

            lock (gate)
            {
                peerState.IsRegistering = false;
                peerState.DisplayName = serviceRegistration.Hostname;
                peerState.Hostname = serviceRegistration.Hostname;
                peerState.Availability = "Registered";
                peerState.Details = $"Certificate exchange succeeded. Linux peer acknowledged this client on auth {serviceRegistration.AuthPort}.";
            }

            RuntimeLog.Write($"Registration succeeded with {peerState.DisplayName}. Remote returned service id {serviceRegistration.ServiceId} host {serviceRegistration.Hostname}.");
            PublishSnapshot(
                $"Registration succeeded with {peerState.DisplayName}.",
                "Discovery and registration live",
                "The next milestone is secure transfer-channel validation and real file movement.");

            StartPeerSecureSession(peerId);
        }
        catch (Exception ex)
        {
            RuntimeLog.Write($"Registration exception for {peerState.DisplayName}: {ex}");
            lock (gate)
            {
                peerState.IsRegistering = false;
                peerState.Availability = "Error";
                peerState.Details = "Registration attempt failed: " + ex.Message;
            }

            PublishSnapshot(
                $"Registration failed for {peerState.DisplayName}.",
                "Discovery and registration live",
                "The peer was discovered, but the auth handshake did not complete.");
        }
        finally
        {
            if (channel is not null)
            {
                await channel.ShutdownAsync();
            }
        }
    }

    private void StartPeerSecureSession(string peerId)
    {
        CancellationTokenSource sessionTokenSource;

        lock (gate)
        {
            if (peerSessionTokens.TryGetValue(peerId, out var existingTokenSource))
            {
                if (!existingTokenSource.IsCancellationRequested)
                {
                    RuntimeLog.Write($"Secure peer session already active for {peerId}.");
                    return;
                }

                existingTokenSource.Dispose();
                peerSessionTokens.Remove(peerId);
            }

            sessionTokenSource = new CancellationTokenSource();
            peerSessionTokens[peerId] = sessionTokenSource;

            if (peers.TryGetValue(peerId, out var peerState))
            {
                peerState.HasActiveSecureSession = true;
                peerState.Availability = "Connected";
                peerState.Details = "Registration succeeded and the secure Warp session is being maintained.";
            }
        }

        RuntimeLog.Write($"Starting secure peer session for {peerId}.");
        PublishSnapshot(
            "Maintaining the secure peer session.",
            "Discovery and registration live",
            "Keeping the Warp channel active so Linux continues to see this client as online.");

        _ = Task.Run(() => MaintainPeerSecureSessionAsync(peerId, sessionTokenSource.Token));
    }

    private async Task MaintainPeerSecureSessionAsync(string peerId, CancellationToken cancellationToken)
    {
        if (localProfile is null)
        {
            return;
        }

        RuntimePeerState peerState;
        lock (gate)
        {
            if (!peers.TryGetValue(peerId, out peerState!))
            {
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(peerState.RemoteCertificatePem))
        {
            RuntimeLog.Write($"Secure peer session aborted for {peerId}: remote certificate missing.");
            return;
        }

        Channel? channel = null;

        try
        {
            var remoteCertificatePem = peerState.RemoteCertificatePem
                ?? throw new InvalidOperationException("The remote certificate is not available for the selected peer.");

            channel = new Channel(
                $"{peerState.Address}:{peerState.TransferPort}",
                new SslCredentials(remoteCertificatePem),
                new[] 
                {
                    new ChannelOption("grpc.keepalive_time_ms", 10_000),
                    new ChannelOption("grpc.keepalive_timeout_ms", 5_000),
                    new ChannelOption("grpc.keepalive_permit_without_calls", 1),
                    new ChannelOption("grpc.http2.max_pings_without_data", 0),
                    new ChannelOption("grpc.http2.min_time_between_pings_ms", 10_000),
                    new ChannelOption("grpc.http2.min_ping_interval_without_data_ms", 5_000)
                });

            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(10));

            var client = new Warp.WarpClient(channel);

            lock (gate)
            {
                if (peers.TryGetValue(peerId, out var mutablePeerState))
                {
                    mutablePeerState.ActiveSecureSessionChannel = channel;
                    mutablePeerState.ActiveSecureSessionClient = client;
                }
            }

            var localLookup = new LookupName
            {
                Id = localProfile.ServiceId,
                ReadableName = localProfile.DisplayName
            };

            var waiting = await client.WaitingForDuplexAsync(localLookup, deadline: DateTime.UtcNow.AddSeconds(10));
            RuntimeLog.Write($"Secure session WaitingForDuplex for {peerId} returned {waiting.Response}.");

            var check = await client.CheckDuplexConnectionAsync(localLookup, deadline: DateTime.UtcNow.AddSeconds(10));
            RuntimeLog.Write($"Secure session CheckDuplexConnection for {peerId} returned {check.Response}.");

            var remoteMachineInfo = await client.GetRemoteMachineInfoAsync(localLookup, deadline: DateTime.UtcNow.AddSeconds(10));
            RuntimeLog.Write($"Secure session remote info for {peerId}: display={remoteMachineInfo.DisplayName}, user={remoteMachineInfo.UserName}, flags={remoteMachineInfo.FeatureFlags}.");

            lock (gate)
            {
                if (peers.TryGetValue(peerId, out var mutablePeerState))
                {
                    mutablePeerState.Availability = "Connected";
                    mutablePeerState.Details = $"Secure Warp session active with {remoteMachineInfo.DisplayName}.";
                }
            }

            PublishSnapshot(
                "Secure peer session active.",
                "Discovery and registration live",
                "Linux should now keep the Windows client visible as an online peer.");

            while (!cancellationToken.IsCancellationRequested)
            {
                await client.PingAsync(localLookup, deadline: DateTime.UtcNow.AddSeconds(10));
                RuntimeLog.Write($"Secure session ping succeeded for {peerId}.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            RuntimeLog.Write($"Secure peer session cancelled for {peerId}.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Write($"Secure peer session failed for {peerId}: {ex}");

            lock (gate)
            {
                if (peers.TryGetValue(peerId, out var mutablePeerState))
                {
                    mutablePeerState.HasActiveSecureSession = false;
                    mutablePeerState.Availability = "Registered";
                    mutablePeerState.Details = "Registration succeeded, but the secure Warp session could not be maintained.";
                }
            }

            PublishSnapshot(
                "Secure peer session failed.",
                "Discovery and registration live",
                "The Linux peer may still drop this client until the Warp session stays active.");
        }
        finally
        {
            lock (gate)
            {
                if (peerSessionTokens.TryGetValue(peerId, out var tokenSource) && tokenSource.Token == cancellationToken)
                {
                    tokenSource.Dispose();
                    peerSessionTokens.Remove(peerId);
                }

                if (peers.TryGetValue(peerId, out var mutablePeerState))
                {
                    mutablePeerState.HasActiveSecureSession = false;

                    if (ReferenceEquals(mutablePeerState.ActiveSecureSessionChannel, channel))
                    {
                        mutablePeerState.ActiveSecureSessionChannel = null;
                        mutablePeerState.ActiveSecureSessionClient = null;
                    }
                }
            }

            if (channel is not null)
            {
                await channel.ShutdownAsync();
            }
        }
    }

    private async Task ReceiveTransferFromPeerAsync(string transferKey)
    {
        if (localProfile is null)
        {
            return;
        }

        RuntimeTransferState transferState;
        RuntimePeerState peerState;

        lock (gate)
        {
            if (!transfers.TryGetValue(transferKey, out transferState!) || !peers.TryGetValue(transferState.PeerId, out peerState!))
            {
                return;
            }

            transferState.Status = "Connecting";
            transferState.Summary = $"Opening the secure stream to {transferState.PeerDisplayName}.";
        }

        PublishSnapshot(
            $"Connecting to {transferState.PeerDisplayName} for {transferState.Name}.",
            "Inbound transfer active",
            "Waiting for the remote Warp stream to start.");

        IncomingTransferWriter? writer = null;

        try
        {
            if (string.IsNullOrWhiteSpace(peerState.RemoteCertificatePem))
            {
                throw new InvalidOperationException("The remote certificate is not available for the selected peer.");
            }

            writer = new IncomingTransferWriter(settings.SaveFolderPath, transferState.TopLevelNames);
            var receiveCompleted = false;

            for (var attempt = 1; attempt <= 5 && !receiveCompleted; attempt++)
            {
                Channel? channel = null;
                var sawAnyChunk = false;

                try
                {
                    channel = new Channel(
                        $"{peerState.Address}:{peerState.TransferPort}",
                        new SslCredentials(peerState.RemoteCertificatePem));

                    await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));

                    var client = new Warp.WarpClient(channel);
                    using var call = client.StartTransfer(
                        new OpInfo
                        {
                            Ident = localProfile.ServiceId,
                            Timestamp = transferState.OpTimestamp,
                            ReadableName = localProfile.HostName,
                            UseCompression = transferState.UseCompression
                        },
                        deadline: DateTime.UtcNow.AddMinutes(5));

                    lock (gate)
                    {
                        transferState.Status = "Receiving";
                        transferState.Summary = $"Receiving {transferState.Name} from {transferState.PeerDisplayName}. Attempt {attempt}.";
                        transferState.ActivityStartedAtUtc ??= DateTimeOffset.UtcNow;
                    }

                    PublishSnapshot(
                        $"Receiving {transferState.Name}.",
                        "Inbound transfer active",
                        "The Windows client is now pulling file chunks from the Linux peer.");

                    while (await call.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        sawAnyChunk = true;
                        writer.WriteChunk(call.ResponseStream.Current);

                        var shouldPublishProgress = false;
                        lock (gate)
                        {
                            transferState.BytesReceived += (ulong)call.ResponseStream.Current.Chunk.Length;
                            transferState.Status = "Receiving";
                            transferState.Summary = $"Received {FormatSize(transferState.BytesReceived)} of {FormatSize(transferState.TotalSize)} from {transferState.PeerDisplayName}.";
                            shouldPublishProgress = ShouldPublishProgressSnapshot(transferState);
                        }

                        if (shouldPublishProgress)
                        {
                            PublishSnapshot(
                                $"Receiving {transferState.Name}.",
                                "Inbound transfer active",
                                "The Windows client is now pulling file chunks from the Linux peer.");
                        }
                    }

                    receiveCompleted = true;
                }
                catch (RpcException ex) when (!sawAnyChunk && attempt < 5 && IsRetryableStartTransferError(ex))
                {
                    RuntimeLog.Write($"Retrying StartTransfer for {transferKey} after attempt {attempt}: {ex.Status.Detail}");
                    await Task.Delay(750);
                }
                finally
                {
                    if (channel is not null)
                    {
                        await channel.ShutdownAsync();
                    }
                }
            }

            if (!receiveCompleted)
            {
                throw new InvalidOperationException("The Linux sender never produced a usable file stream.");
            }

            writer.Complete();

            lock (gate)
            {
                transferState.IsStreamActive = false;
                transferState.Status = "Completed";
                transferState.CompletedAtUtc = DateTimeOffset.UtcNow;
                transferState.Summary = $"Saved to {settings.SaveFolderPath}.";
                transferState.RevealPath = writer.RevealPath;
            }

            RuntimeLog.Write($"Transfer completed successfully: {transferState.Name} from {transferState.PeerDisplayName}.");
            PublishSnapshot(
                $"Transfer completed: {transferState.Name}.",
                "Inbound transfer active",
                "The first Linux-to-Windows file flow completed. The next milestone is Windows-initiated send.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Write($"Inbound transfer exception for {transferKey}: {ex}");
            lock (gate)
            {
                if (transfers.TryGetValue(transferKey, out transferState!))
                {
                    transferState.IsStreamActive = false;
                    transferState.Status = "Failed";
                    transferState.Summary = ex.Message;
                    transferState.CompletedAtUtc = DateTimeOffset.UtcNow;
                }
            }

            PublishSnapshot(
                "An inbound transfer failed.",
                "Inbound transfer active",
                "Check the runtime log and transfer history for the failure details.");
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private void HandleRemoteRegistrationRequest(ServiceRegistration registration)
    {
        var peerId = registration.ServiceId;
        lock (gate)
        {
            if (!peers.TryGetValue(peerId, out var peerState))
            {
                peerState = new RuntimePeerState(peerId);
                peers[peerId] = peerState;
            }

            peerState.DisplayName = registration.Hostname;
            peerState.Hostname = registration.Hostname;
            peerState.Address = string.IsNullOrWhiteSpace(registration.Ip) ? peerState.Address : registration.Ip;
            peerState.TransferPort = (int)registration.Port;
            peerState.AuthenticationPort = (int)registration.AuthPort;
            peerState.ApiVersion = (int)registration.ApiVersion;
            peerState.Availability = "Remote initiated";
            peerState.Details = "Linux Warpinator called RegisterService against this Windows client.";
        }

        RuntimeLog.Write($"Remote RegisterService received from {registration.Hostname} ({registration.ServiceId}).");
        PublishSnapshot(
            "Linux Warpinator registered back to this Windows client.",
            "Discovery and registration live",
            "Registration is bidirectional. Transfer-channel work is next.");
    }

    private RuntimePeerState? TryParsePeer(ServiceInstanceDiscoveryEventArgs eventArgs)
    {
        var instanceName = eventArgs.ServiceInstanceName.ToString();
        if (!instanceName.Contains($".{ServiceType}.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var serviceId = instanceName.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        var records = eventArgs.Message.Answers.Concat(eventArgs.Message.AdditionalRecords).ToList();
        var srvRecord = records.OfType<SRVRecord>().FirstOrDefault(record => NameEquals(record.Name, instanceName));
        var txtRecord = records.OfType<TXTRecord>().FirstOrDefault(record => NameEquals(record.Name, instanceName));

        if (srvRecord is null)
        {
            return null;
        }

        var properties = ParseTxt(txtRecord);
        if (properties.TryGetValue("type", out var recordType) && string.Equals(recordType, "flush", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var address = records.OfType<AddressRecord>()
            .FirstOrDefault(record => NameEquals(record.Name, srvRecord.Target))
            ?.Address
            ?.ToString()
            ?? eventArgs.RemoteEndPoint?.Address.ToString()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var hostname = properties.TryGetValue("hostname", out var hostnameValue)
            ? hostnameValue
            : srvRecord.Target;
        var apiVersion = properties.TryGetValue("api-version", out var apiVersionText) && int.TryParse(apiVersionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedApiVersion)
            ? parsedApiVersion
            : 1;
        var authPort = properties.TryGetValue("auth-port", out var authPortText) && int.TryParse(authPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAuthPort)
            ? parsedAuthPort
            : 0;

        return new RuntimePeerState(serviceId)
        {
            DisplayName = hostname,
            Hostname = hostname,
            Address = address,
            TransferPort = srvRecord.Port,
            AuthenticationPort = authPort,
            ApiVersion = apiVersion,
            Availability = "Discovered",
            Details = $"api v{apiVersion}, transfer {srvRecord.Port}, auth {authPort}"
        };
    }

    private Dictionary<string, string> ParseTxt(TXTRecord? txtRecord)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (txtRecord is null)
        {
            return properties;
        }

        foreach (var entry in txtRecord.Strings)
        {
            var parts = entry.Split('=', 2);
            if (parts.Length == 2)
            {
                properties[parts[0]] = parts[1];
            }
        }

        return properties;
    }

    private static bool NameEquals(string left, string right)
    {
        return string.Equals(left.TrimEnd('.'), right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ChannelOption> CreateGrpcServerOptions()
    {
        return new[]
        {
            new ChannelOption("grpc.keepalive_time_ms", 10_000),
            new ChannelOption("grpc.keepalive_timeout_ms", 5_000),
            new ChannelOption("grpc.keepalive_permit_without_calls", 1),
            new ChannelOption("grpc.http2.max_pings_without_data", 0),
            new ChannelOption("grpc.http2.min_time_between_pings_ms", 10_000),
            new ChannelOption("grpc.http2.min_ping_interval_without_data_ms", 5_000)
        };
    }

    private static string TryParseContextAddress(string remotePeerDescriptor)
    {
        if (string.IsNullOrWhiteSpace(remotePeerDescriptor))
        {
            return string.Empty;
        }

        const string ipv4Prefix = "ipv4:";
        const string ipv6Prefix = "ipv6:";

        if (remotePeerDescriptor.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var addressPort = remotePeerDescriptor[ipv4Prefix.Length..];
            var lastColon = addressPort.LastIndexOf(':');
            return lastColon > 0 ? addressPort[..lastColon] : addressPort;
        }

        if (remotePeerDescriptor.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var addressPort = remotePeerDescriptor[ipv6Prefix.Length..];
            var lastColon = addressPort.LastIndexOf(':');
            return lastColon > 0 ? addressPort[..lastColon].Trim('[', ']') : addressPort.Trim('[', ']');
        }

        return string.Empty;
    }

    private void PublishSnapshot(string statusMessage, string currentPhase, string nextStep)
    {
        var profile = localProfile;
        var transferSnapshots = transfers.Values
            .OrderByDescending(transfer => transfer.StartedAtUtc)
            .Take(8)
            .Select(transfer => new WarpinatorRuntimeTransfer
            {
                Name = transfer.Name,
                Peer = string.IsNullOrWhiteSpace(transfer.PeerDisplayName) ? transfer.PeerId : transfer.PeerDisplayName,
                Direction = transfer.IsInbound ? "Receiving" : "Sending",
                Status = transfer.Status,
                Summary = transfer.Summary,
                ProgressSummary = GetTransferProgressSummary(transfer),
                SpeedSummary = GetTransferSpeedSummary(transfer),
                EtaSummary = GetTransferEtaSummary(transfer),
                ProgressPercent = GetTransferProgressPercent(transfer),
                IsActive = IsTransferActive(transfer),
                RevealPath = transfer.RevealPath,
                CanReveal = CanRevealTransfer(transfer)
            })
            .ToArray();

        var snapshot = new WarpinatorRuntimeSnapshot
        {
            StatusMessage = statusMessage,
            CurrentPhase = currentPhase,
            TransferReadiness = started
                ? "Ready to send and receive files with nearby Linux devices."
                : "Starting the local transfer service.",
            NextStep = nextStep,
            PeerCountSummary = peers.Count.ToString(CultureInfo.InvariantCulture),
            TransferCountSummary = transfers.Values.Count(transfer => string.Equals(transfer.Status, "Completed", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture),
            DependencyBudgetSummary = "5 external",
            ProtocolPortsSummary = profile is null
                ? $"Transfer {WarpinatorProtocolDefaults.TransferPort}, auth {WarpinatorProtocolDefaults.AuthenticationPort}"
                : $"Transfer {profile.TransferPort}, auth {profile.AuthenticationPort}",
            ApiVersionsSummary = string.Join(", ", WarpinatorProtocolDefaults.SupportedApiVersions),
            GroupCodeSummary = profile?.GroupCode ?? settings.GroupCode,
            RpcSurfaceSummary = $"{WarpinatorProtocolDefaults.TransferServiceMethods.Count} transfer RPCs, {WarpinatorProtocolDefaults.RegistrationServiceMethods.Count} registration RPCs",
            SaveFolderSummary = profile?.SaveFolderPath ?? settings.SaveFolderPath,
            Peers = peers.Values
                .OrderBy(peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(peer => new WarpinatorRuntimePeer
                {
                    Id = peer.Id,
                    DisplayName = string.IsNullOrWhiteSpace(peer.DisplayName) ? peer.Id : peer.DisplayName,
                    Endpoint = string.IsNullOrWhiteSpace(peer.Address) ? "No endpoint yet" : $"{peer.Address}:{peer.TransferPort}",
                    Details = peer.Details,
                    Availability = peer.Availability
                })
                .Cast<WarpinatorRuntimePeer>()
                .ToArray(),
            Transfers = transferSnapshots
        };

        RuntimeLog.Write($"Snapshot published: phase={snapshot.CurrentPhase}, peers={snapshot.PeerCountSummary}, status={snapshot.StatusMessage}");
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private sealed class RuntimePeerState
    {
        public RuntimePeerState(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public string DisplayName { get; set; } = string.Empty;

        public string Hostname { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public int TransferPort { get; set; }

        public int AuthenticationPort { get; set; }

        public int ApiVersion { get; set; }

        public string Availability { get; set; } = "Pending";

        public string Details { get; set; } = string.Empty;

        public bool IsRegistering { get; set; }

        public string? RemoteCertificatePem { get; set; }

        public bool HasActiveSecureSession { get; set; }

        public Channel? ActiveSecureSessionChannel { get; set; }

        public Warp.WarpClient? ActiveSecureSessionClient { get; set; }
    }

    private sealed class RuntimeTransferState
    {
        public RuntimeTransferState(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public string PeerId { get; set; } = string.Empty;

        public string PeerDisplayName { get; set; } = string.Empty;

        public string Name { get; set; } = "Incoming transfer";

        public string? LocalFilePath { get; set; }

        public string? RevealPath { get; set; }

        public ulong OpTimestamp { get; set; }

        public string OperationIdent { get; set; } = string.Empty;

        public string OperationReadableName { get; set; } = string.Empty;

        public bool UseCompression { get; set; }

        public ulong TotalSize { get; set; }

        public ulong BytesReceived { get; set; }

        public ulong BytesSent { get; set; }

        public ulong TotalCount { get; set; }

        public string[] TopLevelNames { get; set; } = Array.Empty<string>();

        public string MimeType { get; set; } = "application/octet-stream";

        public string Status { get; set; } = "Pending";

        public string Summary { get; set; } = string.Empty;

        public bool IsInbound { get; set; }

        public bool IsStreamActive { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset? ActivityStartedAtUtc { get; set; }

        public DateTimeOffset? LastProgressSnapshotAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }
    }

    private static string CreateTransferKey(string peerId, ulong timestamp)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{peerId}:{timestamp}");
    }

    private static string FormatSize(ulong bytes)
    {
        return ByteSizeFormatter.Format(bytes);
    }

    private static bool IsTransferActive(RuntimeTransferState transfer)
    {
        return !string.Equals(transfer.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transfer.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transfer.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanRevealTransfer(RuntimeTransferState transfer)
    {
        return transfer.IsInbound
            && string.Equals(transfer.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(transfer.RevealPath)
            && (File.Exists(transfer.RevealPath) || Directory.Exists(transfer.RevealPath));
    }

    private static ulong GetTransferredBytes(RuntimeTransferState transfer)
    {
        return transfer.IsInbound ? transfer.BytesReceived : transfer.BytesSent;
    }

    private static double GetTransferProgressPercent(RuntimeTransferState transfer)
    {
        if (transfer.TotalSize == 0)
        {
            return 0;
        }

        return Math.Clamp(GetTransferredBytes(transfer) * 100d / transfer.TotalSize, 0d, 100d);
    }

    private static string GetTransferProgressSummary(RuntimeTransferState transfer)
    {
        if (transfer.TotalSize == 0)
        {
            return "Preparing transfer";
        }

        return $"{FormatSize(GetTransferredBytes(transfer))} / {FormatSize(transfer.TotalSize)}";
    }

    private static string GetTransferSpeedSummary(RuntimeTransferState transfer)
    {
        var speedBytes = GetTransferSpeedBytesPerSecond(transfer);
        if (speedBytes <= 0)
        {
            return IsTransferActive(transfer)
                ? "Waiting for live throughput"
                : "Transfer idle";
        }

        return $"{ByteSizeFormatter.Format((ulong)Math.Round(speedBytes))}/s";
    }

    private static string GetTransferEtaSummary(RuntimeTransferState transfer)
    {
        if (!IsTransferActive(transfer))
        {
            return "Finished";
        }

        if (string.Equals(transfer.Status, "Awaiting receiver", StringComparison.OrdinalIgnoreCase))
        {
            return "Pending Linux approval";
        }

        var speedBytes = GetTransferSpeedBytesPerSecond(transfer);
        var remainingBytes = transfer.TotalSize > GetTransferredBytes(transfer)
            ? transfer.TotalSize - GetTransferredBytes(transfer)
            : 0;

        if (remainingBytes == 0 && transfer.TotalSize > 0)
        {
            return "Wrapping up";
        }

        if (speedBytes <= 0 || remainingBytes == 0)
        {
            return "Calculating ETA";
        }

        return FormatEta(TimeSpan.FromSeconds(remainingBytes / speedBytes));
    }

    private static double GetTransferSpeedBytesPerSecond(RuntimeTransferState transfer)
    {
        if (transfer.ActivityStartedAtUtc is null)
        {
            return 0;
        }

        var elapsed = DateTimeOffset.UtcNow - transfer.ActivityStartedAtUtc.Value;
        var transferredBytes = GetTransferredBytes(transfer);
        if (elapsed <= TimeSpan.Zero || transferredBytes == 0)
        {
            return 0;
        }

        return transferredBytes / elapsed.TotalSeconds;
    }

    private static string FormatEta(TimeSpan timeRemaining)
    {
        if (timeRemaining.TotalSeconds < 1)
        {
            return "Less than 1s left";
        }

        if (timeRemaining.TotalMinutes < 1)
        {
            return $"{Math.Ceiling(timeRemaining.TotalSeconds):0}s left";
        }

        if (timeRemaining.TotalHours < 1)
        {
            return $"{Math.Floor(timeRemaining.TotalMinutes):0}m {timeRemaining.Seconds:00}s left";
        }

        return $"{(int)timeRemaining.TotalHours}h {timeRemaining.Minutes:00}m left";
    }

    private static bool ShouldPublishProgressSnapshot(RuntimeTransferState transfer)
    {
        var now = DateTimeOffset.UtcNow;
        if (transfer.LastProgressSnapshotAtUtc is { } lastSnapshot
            && now - lastSnapshot < TimeSpan.FromMilliseconds(300))
        {
            return false;
        }

        transfer.LastProgressSnapshotAtUtc = now;
        return true;
    }

    private static bool IsRetryableStartTransferError(RpcException exception)
    {
        var detail = exception.Status.Detail ?? string.Empty;
        return detail.Contains("file_send_cancellable", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("NoneType", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("unknowns op", StringComparison.OrdinalIgnoreCase);
    }
}



