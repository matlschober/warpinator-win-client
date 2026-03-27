using WarpinatorWinClient.Core.Models;

namespace WarpinatorWinClient.Core.Services;

public interface IPeerRegistrationService
{
    Task<bool> TryRegisterAsync(DiscoveredPeer peer, CancellationToken cancellationToken);
}
