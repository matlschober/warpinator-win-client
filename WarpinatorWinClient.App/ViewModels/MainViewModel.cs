using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WarpinatorWinClient.App.Interop;
using WarpinatorWinClient.App.Models;
using WarpinatorWinClient.Core.Configuration;
namespace WarpinatorWinClient.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string statusMessage = string.Empty;
    private string transferReadiness = string.Empty;
    private string peerCountSummary = string.Empty;
    private string transferCountSummary = string.Empty;
    private string saveFolderSummary = string.Empty;
    private string compatibilitySummary = "Move files between this PC and Linux peers on the same network.";
    private string activeTransferName = "No active transfer";
    private string activeTransferPeer = "Waiting for a connected peer";
    private string activeTransferDirection = "Send or receive";
    private string activeTransferStatus = "Idle";
    private string activeTransferSummary = "Choose files to send, or receive them from a connected device.";
    private string activeTransferProgressSummary = "Waiting for a transfer";
    private string activeTransferSpeedSummary = "Waiting";
    private string activeTransferEtaSummary = "No ETA";
    private double activeTransferProgressPercent;
    private bool hasActiveTransfer;
    private bool isDropTargetActive;
    private bool canSearch;
    private string searchButtonText = "Search";
    private string connectionSummary = "Starting…";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanSearch
    {
        get => canSearch;
        private set => SetField(ref canSearch, value);
    }

    public string SearchButtonText
    {
        get => searchButtonText;
        private set => SetField(ref searchButtonText, value);
    }

    public string ConnectionSummary
    {
        get => connectionSummary;
        private set => SetField(ref connectionSummary, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CompatibilitySummary
    {
        get => compatibilitySummary;
        private set => SetField(ref compatibilitySummary, value);
    }

    public string TransferReadiness
    {
        get => transferReadiness;
        private set => SetField(ref transferReadiness, value);
    }

    public string PeerCountSummary
    {
        get => peerCountSummary;
        private set => SetField(ref peerCountSummary, value);
    }

    public string TransferCountSummary
    {
        get => transferCountSummary;
        private set => SetField(ref transferCountSummary, value);
    }

    public string SaveFolderSummary
    {
        get => saveFolderSummary;
        private set => SetField(ref saveFolderSummary, value);
    }

    public string ActiveTransferName
    {
        get => activeTransferName;
        private set => SetField(ref activeTransferName, value);
    }

    public string ActiveTransferPeer
    {
        get => activeTransferPeer;
        private set => SetField(ref activeTransferPeer, value);
    }

    public string ActiveTransferDirection
    {
        get => activeTransferDirection;
        private set => SetField(ref activeTransferDirection, value);
    }

    public string ActiveTransferStatus
    {
        get => activeTransferStatus;
        private set => SetField(ref activeTransferStatus, value);
    }

    public string ActiveTransferSummary
    {
        get => activeTransferSummary;
        private set => SetField(ref activeTransferSummary, value);
    }

    public string ActiveTransferProgressSummary
    {
        get => activeTransferProgressSummary;
        private set => SetField(ref activeTransferProgressSummary, value);
    }

    public string ActiveTransferSpeedSummary
    {
        get => activeTransferSpeedSummary;
        private set => SetField(ref activeTransferSpeedSummary, value);
    }

    public string ActiveTransferEtaSummary
    {
        get => activeTransferEtaSummary;
        private set => SetField(ref activeTransferEtaSummary, value);
    }

    public double ActiveTransferProgressPercent
    {
        get => activeTransferProgressPercent;
        private set => SetField(ref activeTransferProgressPercent, value);
    }

    public bool HasActiveTransfer
    {
        get => hasActiveTransfer;
        private set => SetField(ref hasActiveTransfer, value);
    }

    public bool IsDropTargetActive
    {
        get => isDropTargetActive;
        private set => SetField(ref isDropTargetActive, value);
    }

    public ObservableCollection<PeerSummary> Peers { get; } = new();

    public ObservableCollection<TransferSummary> RecentTransfers { get; } = new();

    public static MainViewModel CreateDesignState()
    {
        var viewModel = new MainViewModel
        {
            StatusMessage = "Starting the local transfer service.",
            CompatibilitySummary = "Move files between this PC and Linux peers on the same network.",
            TransferReadiness = "Looking for nearby Linux peers.",
            PeerCountSummary = "0",
            TransferCountSummary = "0",
            SaveFolderSummary = new AppSettings().SaveFolderPath
        };

        viewModel.Peers.Add(new PeerSummary
        {
            DisplayName = "Waiting for runtime startup",
            Endpoint = "Discovery has not started yet",
            Details = "Nearby Linux devices will appear here once the app is online.",
            Availability = "Pending"
        });

        viewModel.RecentTransfers.Add(new TransferSummary
        {
            Name = "No transfers yet",
            Peer = "History will appear here",
            Direction = "Idle",
            Status = "Waiting",
            Summary = "Finished and active transfers will appear here once files start moving.",
            ProgressSummary = "0 / 0",
            SpeedSummary = "Waiting",
            EtaSummary = "No ETA",
            ProgressPercent = 0,
            IsActive = false,
            RevealPath = null,
            CanReveal = false
        });

        return viewModel;
    }

    public void ApplyRuntimeSnapshot(WarpinatorRuntimeSnapshot snapshot)
    {
        CanSearch = snapshot.IsRunning && !snapshot.IsSearching;
        SearchButtonText = snapshot.IsSearching ? "Searching…" : "Search";
        var connectedCount = snapshot.Peers.Count(peer => peer.Availability == "Connected");
        ConnectionSummary = !snapshot.IsRunning ? "Service offline"
            : connectedCount > 0 ? $"{connectedCount} device(s) connected"
            : snapshot.IsSearching ? "Searching the network…" : "No connected devices";
        StatusMessage = snapshot.StatusMessage;
        TransferReadiness = snapshot.TransferReadiness;
        PeerCountSummary = snapshot.PeerCountSummary;
        TransferCountSummary = snapshot.TransferCountSummary;
        SaveFolderSummary = snapshot.SaveFolderSummary;

        Peers.Clear();
        foreach (var peer in snapshot.Peers)
        {
            Peers.Add(new PeerSummary
            {
                DisplayName = peer.DisplayName,
                Endpoint = peer.Endpoint,
                Details = peer.Details,
                Availability = peer.Availability
            });
        }

        if (Peers.Count == 0)
        {
            Peers.Add(new PeerSummary
            {
                DisplayName = "No peers discovered yet",
                Endpoint = "Still browsing the LAN",
                Details = "Make sure Linux Warpinator is open, on the same LAN, and using the same group code.",
                Availability = "Searching"
            });
        }

        RecentTransfers.Clear();
        foreach (var transfer in snapshot.Transfers)
        {
            RecentTransfers.Add(new TransferSummary
            {
                Name = transfer.Name,
                Peer = transfer.Peer,
                Direction = transfer.Direction,
                Status = transfer.Status,
                Summary = transfer.Summary,
                ProgressSummary = transfer.ProgressSummary,
                SpeedSummary = transfer.SpeedSummary,
                EtaSummary = transfer.EtaSummary,
                ProgressPercent = transfer.ProgressPercent,
                IsActive = transfer.IsActive,
                RevealPath = transfer.RevealPath,
                CanReveal = transfer.CanReveal
            });
        }

        if (RecentTransfers.Count == 0)
        {
            RecentTransfers.Add(new TransferSummary
            {
                Name = "No transfers yet",
                Peer = "Waiting for the first move",
                Direction = "Idle",
                Status = "Waiting",
                Summary = "Drop a file into the send stage or wait for Linux to initiate a receive.",
                ProgressSummary = "0 / 0",
                SpeedSummary = "Waiting",
                EtaSummary = "No ETA",
                ProgressPercent = 0,
                IsActive = false,
                RevealPath = null,
                CanReveal = false
            });
        }

        var activeTransfer = snapshot.Transfers.FirstOrDefault(transfer => transfer.IsActive);
        if (activeTransfer is null)
        {
            HasActiveTransfer = false;
            ActiveTransferName = "No active transfer";
            ActiveTransferPeer = snapshot.Peers.FirstOrDefault(peer => peer.Availability == "Connected")?.DisplayName ?? "Waiting for a connected Linux peer";
            ActiveTransferDirection = "Send or receive";
            ActiveTransferStatus = "Idle";
            ActiveTransferSummary = "Choose files to send, or receive them from a connected device.";
            ActiveTransferProgressSummary = "Waiting for a transfer";
            ActiveTransferSpeedSummary = "Waiting";
            ActiveTransferEtaSummary = "No ETA";
            ActiveTransferProgressPercent = 0;
        }
        else
        {
            HasActiveTransfer = true;
            ActiveTransferName = activeTransfer.Name;
            ActiveTransferPeer = activeTransfer.Peer;
            ActiveTransferDirection = activeTransfer.Direction;
            ActiveTransferStatus = activeTransfer.Status;
            ActiveTransferSummary = activeTransfer.Summary;
            ActiveTransferProgressSummary = activeTransfer.ProgressSummary;
            ActiveTransferSpeedSummary = activeTransfer.SpeedSummary;
            ActiveTransferEtaSummary = activeTransfer.EtaSummary;
            ActiveTransferProgressPercent = activeTransfer.ProgressPercent;
        }
    }

    public void SetDropTargetActive(bool isActive)
    {
        IsDropTargetActive = isActive;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
