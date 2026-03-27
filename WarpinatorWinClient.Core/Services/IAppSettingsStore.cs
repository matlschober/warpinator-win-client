using WarpinatorWinClient.Core.Configuration;

namespace WarpinatorWinClient.Core.Services;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
