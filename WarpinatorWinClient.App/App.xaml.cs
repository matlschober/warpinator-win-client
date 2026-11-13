using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WarpinatorWinClient.App.Interop;
using WarpinatorWinClient.Core.Configuration;
using WarpinatorWinClient.Core.Services;

namespace WarpinatorWinClient.App;

public partial class App : Application
{
    private WarpinatorRuntime? runtime;

    public WarpinatorRuntime Runtime => runtime ??= new WarpinatorRuntime(CurrentSettings);

    public AppSettings CurrentSettings { get; private set; } = new();

    public IAppSettingsStore SettingsStore { get; } = new JsonAppSettingsStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarpinatorWinClient", "settings.json"));

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            CurrentSettings = await SettingsStore.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An unreadable setting must never silently enable automatic receiving.
            CurrentSettings = new AppSettings();
            RuntimeLog.Write("Settings could not be loaded; receive approval remains enabled: " + ex);
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    public async Task SetReceiveApprovalAsync(bool required)
    {
        var updated = CurrentSettings with { RequireReceiveApproval = required };
        await SettingsStore.SaveAsync(updated, CancellationToken.None);
        CurrentSettings = updated;
        Runtime.SetReceiveApprovalRequired(required);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (runtime is not null)
        {
            await runtime.StopAsync();
            runtime.Dispose();
        }
        base.OnExit(e);
    }
}
