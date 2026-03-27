using System;
using System.Threading.Tasks;
using Grpc.Core;

namespace WarpinatorWinClient.App.Interop;

public sealed class WarpServiceCompatImpl : Warp.WarpBase
{
    private static readonly VoidType VoidResponse = new();

    private readonly WarpinatorRuntime runtime;
    private readonly LocalMachineProfile localProfile;

    public WarpServiceCompatImpl(WarpinatorRuntime runtime, LocalMachineProfile localProfile)
    {
        this.runtime = runtime;
        this.localProfile = localProfile;
    }

    public override Task<VoidType> Ping(LookupName request, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Id, request.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp Ping from {request.ReadableName} ({request.Id}).");
        return Task.FromResult(VoidResponse);
    }

    public override Task<HaveDuplex> CheckDuplexConnection(LookupName request, ServerCallContext context)
    {
        RuntimeLog.Write($"Warp CheckDuplexConnection from {request.ReadableName} ({request.Id}).");
        return Task.FromResult(new HaveDuplex
        {
            Response = runtime.CanAcceptDuplexProbe(request.Id, request.ReadableName, context.Peer)
        });
    }

    public override Task<HaveDuplex> WaitingForDuplex(LookupName request, ServerCallContext context)
    {
        RuntimeLog.Write($"Warp WaitingForDuplex from {request.ReadableName} ({request.Id}).");
        return Task.FromResult(new HaveDuplex
        {
            Response = runtime.CanAcceptDuplexProbe(request.Id, request.ReadableName, context.Peer)
        });
    }

    public override Task<RemoteMachineInfo> GetRemoteMachineInfo(LookupName request, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Id, request.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp GetRemoteMachineInfo from {request.ReadableName} ({request.Id}).");
        return Task.FromResult(new RemoteMachineInfo
        {
            DisplayName = localProfile.DisplayName,
            UserName = Environment.UserName,
            FeatureFlags = 1
        });
    }

    public override Task GetRemoteMachineAvatar(LookupName request, IServerStreamWriter<RemoteMachineAvatar> responseStream, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Id, request.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp GetRemoteMachineAvatar from {request.ReadableName} ({request.Id}).");
        return Task.CompletedTask;
    }

    public override Task<VoidType> ProcessTransferOpRequest(TransferOpRequest request, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Info.Ident, request.Info.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp ProcessTransferOpRequest from {request.Info.ReadableName} ({request.Info.Ident}) for receiver {request.Receiver} at {request.Info.Timestamp}.");
        runtime.AcceptIncomingTransferRequest(request);
        return Task.FromResult(VoidResponse);
    }

    public override Task<VoidType> PauseTransferOp(OpInfo request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Pause is not implemented yet."));
    }

    public override Task<VoidType> SendTextMessage(TextMessage request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Text messages are not implemented yet."));
    }

    public override Task StartTransfer(OpInfo request, IServerStreamWriter<FileChunk> responseStream, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Ident, request.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp StartTransfer from {request.ReadableName} ({request.Ident}) at {request.Timestamp}.");
        return runtime.StreamOutgoingTransferAsync(request, responseStream);
    }

    public override Task<VoidType> CancelTransferOpRequest(OpInfo request, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Ident, request.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp CancelTransferOpRequest from {request.ReadableName} ({request.Ident}) at {request.Timestamp}.");
        runtime.HandleRemoteTransferCancellation(request);
        return Task.FromResult(VoidResponse);
    }

    public override Task<VoidType> StopTransfer(StopInfo request, ServerCallContext context)
    {
        runtime.ObservePeerProbe(request.Info.Ident, request.Info.ReadableName, context.Peer);
        RuntimeLog.Write($"Warp StopTransfer from {request.Info.ReadableName} ({request.Info.Ident}) at {request.Info.Timestamp}. Error={request.Error}.");
        runtime.HandleRemoteTransferStop(request);
        return Task.FromResult(VoidResponse);
    }
}
