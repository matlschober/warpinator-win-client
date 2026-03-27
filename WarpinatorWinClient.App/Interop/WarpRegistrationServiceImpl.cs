using System;
using System.Threading.Tasks;
using Grpc.Core;

namespace WarpinatorWinClient.App.Interop;

public sealed class WarpRegistrationServiceImpl : WarpRegistration.WarpRegistrationBase
{
    private readonly LocalMachineProfile localProfile;
    private readonly WarpCertificateManager certificateManager;
    private readonly Action<ServiceRegistration> remoteRegistrationHandler;

    public WarpRegistrationServiceImpl(
        LocalMachineProfile localProfile,
        WarpCertificateManager certificateManager,
        Action<ServiceRegistration> remoteRegistrationHandler)
    {
        this.localProfile = localProfile;
        this.certificateManager = certificateManager;
        this.remoteRegistrationHandler = remoteRegistrationHandler;
    }

    public override Task<RegResponse> RequestCertificate(RegRequest request, ServerCallContext context)
    {
        return Task.FromResult(new RegResponse
        {
            LockedCert = certificateManager.GetEncodedLocalCertificate(localProfile.GroupCode)
        });
    }

    public override Task<ServiceRegistration> RegisterService(ServiceRegistration request, ServerCallContext context)
    {
        remoteRegistrationHandler(request);

        return Task.FromResult(new ServiceRegistration
        {
            ServiceId = localProfile.ServiceId,
            Ip = localProfile.Ipv4Address.ToString(),
            Port = (uint)localProfile.TransferPort,
            Hostname = localProfile.HostName,
            ApiVersion = (uint)localProfile.ApiVersion,
            AuthPort = (uint)localProfile.AuthenticationPort,
            Ipv6 = string.Empty
        });
    }
}
