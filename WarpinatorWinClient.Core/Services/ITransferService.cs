using WarpinatorWinClient.Core.Models;

namespace WarpinatorWinClient.Core.Services;

public interface ITransferService
{
    Task<TransferTicket> QueueTransferAsync(TransferIntent intent, CancellationToken cancellationToken);
}
