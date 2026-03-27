using WarpinatorWinClient.Core.Models;

namespace WarpinatorWinClient.Core.Services;

public interface IPeerDiscoveryService
{
    Task<IReadOnlyList<DiscoveredPeer>> DiscoverAsync(CancellationToken cancellationToken);
}
