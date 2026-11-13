# Warpinator Windows Client

A lightweight Windows 11 desktop client that can discover Linux Warpinator peers on the same LAN and transfer files in both directions.

## Status

Current status:

- Bidirectional file transfer is implemented; live Linux-to-Windows FLAC receive and automatic reconnection after a Windows restart are verified
- Receive completion follows Warpinator's stream status and item count; advertised sizes can include disk allocation overhead
- Peer discovery, registration, secure session maintenance, send, and receive are implemented
- The current UI supports progress, ETA, drag-and-drop sending, and revealing received files in Explorer
- Folder transfer, queue depth, richer approvals, and notifications are still planned

## Features

- Discover Linux Warpinator peers over the local network
- Search again with the Search button; automatic discovery retries every 10 seconds
- Pair using Warpinator-compatible registration and certificate exchange
- Recover a stale Linux return connection after a Windows restart with a targeted registration refresh
- Send files from Windows to Linux
- Receive files from Linux to Windows
- Show live transfer progress and estimated time remaining
- Drag and drop files into the app to send them
- Reveal completed received files in Windows Explorer

## Requirements

- Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) to build from source (includes the Windows Desktop Runtime on Windows)
- .NET 10 Windows Desktop Runtime to run the framework-dependent published app
- A Linux machine running Warpinator on the same LAN
- Matching Warpinator group code on both sides

## Build

`global.json` selects a stable .NET 10 SDK and allows newer .NET 10 feature bands and patches.

```powershell
dotnet restore .\WarpinatorWinClient.sln
dotnet build .\WarpinatorWinClient.sln
```

## Run

```powershell
dotnet run --project .\WarpinatorWinClient.App\WarpinatorWinClient.App.csproj
```

Run the isolated TLS/gRPC receive regression checks (no Linux peer required):

```powershell
dotnet run --project .\WarpinatorWinClient.InteropTests\WarpinatorWinClient.InteropTests.csproj -c Release
```

For a live discovery and secure connection check against Linux, close the Windows app to free its ports, leave Warpinator open on Linux, then run:

```powershell
dotnet run --project .\WarpinatorWinClient.InteropTests\WarpinatorWinClient.InteropTests.csproj -c Release -- --discover
```

The check waits up to 30 seconds after the initial search for a connected peer, then repeats the search to verify that the connection is preserved. Runtime diagnostics are saved to `%LOCALAPPDATA%\WarpinatorWinClient\runtime.log`.

## Publish

Framework-dependent Windows x64 publish:

```powershell
dotnet publish .\WarpinatorWinClient.App\WarpinatorWinClient.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
```

The GitHub Actions workflow also produces a zipped Windows artifact automatically.

## Project Layout

- `WarpinatorWinClient.App`: WPF desktop application
- `WarpinatorWinClient.Core`: shared models, settings, protocol defaults, and service contracts
- `agents.md`: living project and status document
- `dependencies.md`: living dependency document

## Known Limitations

- Folder transfer is not complete yet
- Multi-file sending currently runs sequentially
- Symbolic-link transfers are not supported
- The protocol carries no exact per-file lengths or checksums. Receive validation detects gRPC errors and missing items; advertised disk-space totals cannot prove byte-level completeness.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
