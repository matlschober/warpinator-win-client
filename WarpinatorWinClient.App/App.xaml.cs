using System.Windows;
using WarpinatorWinClient.App.Interop;

namespace WarpinatorWinClient.App;

public partial class App : Application
{
    public WarpinatorRuntime Runtime { get; } = new();

    protected override async void OnExit(ExitEventArgs e)
    {
        await Runtime.StopAsync();
        Runtime.Dispose();
        base.OnExit(e);
    }
}
