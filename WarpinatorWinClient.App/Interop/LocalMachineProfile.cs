using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WarpinatorWinClient.Core.Configuration;

namespace WarpinatorWinClient.App.Interop;

public sealed class LocalMachineProfile
{
    private LocalMachineProfile(
        string serviceId,
        string hostName,
        string displayName,
        IPAddress ipv4Address,
        int transferPort,
        int authenticationPort,
        int apiVersion,
        string groupCode,
        string saveFolderPath)
    {
        ServiceId = serviceId;
        HostName = hostName;
        DisplayName = displayName;
        Ipv4Address = ipv4Address;
        TransferPort = transferPort;
        AuthenticationPort = authenticationPort;
        ApiVersion = apiVersion;
        GroupCode = groupCode;
        SaveFolderPath = saveFolderPath;
    }

    public string ServiceId { get; }

    public string HostName { get; }

    public string DisplayName { get; }

    public IPAddress Ipv4Address { get; }

    public int TransferPort { get; }

    public int AuthenticationPort { get; }

    public int ApiVersion { get; }

    public string GroupCode { get; }

    public string SaveFolderPath { get; }

    public static LocalMachineProfile Create(AppSettings settings)
    {
        var ipv4Address = GetPrimaryIpv4Address();
        var serviceId = GetOrCreateServiceId();
        var hostName = Dns.GetHostName();

        return new LocalMachineProfile(
            serviceId,
            hostName,
            settings.DisplayName,
            ipv4Address,
            settings.TransferPort,
            settings.AuthenticationPort,
            apiVersion: 2,
            settings.GroupCode,
            settings.SaveFolderPath);
    }

    private static string GetOrCreateServiceId()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarpinatorWinClient");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "device-id.txt");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        var created = Guid.NewGuid().ToString("D");
        File.WriteAllText(path, created);
        return created;
    }

    private static IPAddress GetPrimaryIpv4Address()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToList();

        foreach (var networkInterface in interfaces)
        {
            var ipProperties = networkInterface.GetIPProperties();
            var candidate = ipProperties.UnicastAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));

            if (candidate is not null)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No usable IPv4 address was found for LAN discovery.");
    }
}
