using System;
using System.Windows;

namespace WarpinatorWinClient.App;

public partial class SettingsWindow : Window
{
    private bool saving;

    public SettingsWindow(Window owner, bool requireReceiveApproval)
    {
        Owner = owner;
        Resources.MergedDictionaries.Add(owner.Resources);
        InitializeComponent();
        ReceiveApprovalCheckBox.IsChecked = requireReceiveApproval;
        Closing += (_, e) => e.Cancel = saving;
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        SaveSettingsButton.IsEnabled = false;
        SaveSettingsButton.Content = "Saving…";
        saving = true;
        try
        {
            await ((App)Application.Current).SetReceiveApprovalAsync(ReceiveApprovalCheckBox.IsChecked == true);
            saving = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            saving = false;
            MessageBox.Show(this, ex.Message, "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            SaveSettingsButton.IsEnabled = true;
            SaveSettingsButton.Content = "Save";
        }
    }
}
