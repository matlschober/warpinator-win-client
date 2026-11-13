# Warpinator Windows Client

A lightweight Windows 11 desktop client that can discover Linux Warpinator peers on the same LAN and transfer files in both directions.

## Status

Current status:

- Bidirectional file transfer is implemented; Linux-to-Windows transfers have previously succeeded on a real LAN
- A receive compatibility fix for duplicate Linux peer registration is awaiting a fresh live transfer check
- Peer discovery, registration, secure session maintenance, send, and receive are implemented
- The current UI supports progress, ETA, drag-and-drop sending, and revealing received files in Explorer
- Folder transfer, queue depth, richer approvals, and notifications are still planned

## Features

- Discover Linux Warpinator peers over the local network
- Pair using Warpinator-compatible registration and certificate exchange
- Send files from Windows to Linux
- Receive files from Linux to Windows
- Show live transfer progress and estimated time remaining
- Drag and drop files into the app to send them
- Reveal completed received files in Windows Explorer

## Requirements

- Windows 11
- .NET SDK 7.0.x to build from source
- A Linux machine running Warpinator on the same LAN
- Matching Warpinator group code on both sides

## Build

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
- The project currently targets `.NET 7`; upgrading later is expected

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
