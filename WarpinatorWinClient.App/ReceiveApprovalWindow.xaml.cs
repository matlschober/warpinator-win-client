using System.Windows;
using WarpinatorWinClient.App.Interop;

namespace WarpinatorWinClient.App;

public partial class ReceiveApprovalWindow : Window
{
    public ReceiveApprovalWindow(Window owner, WarpinatorRuntimeTransfer request, string saveFolder)
    {
        Owner = owner;
        Resources.MergedDictionaries.Add(owner.Resources);
        InitializeComponent();
        SenderText.Text = $"{request.Peer} wants to send you files.";
        FileNameText.Text = request.Name;
        FileNameText.ToolTip = request.Name;
        ItemSummaryText.Text = request.ItemSummary;
        SaveFolderText.Text = saveFolder;
        SaveFolderText.ToolTip = saveFolder;
    }

    private void AcceptClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    public void Dismiss() { if (IsVisible) DialogResult = false; }
}
