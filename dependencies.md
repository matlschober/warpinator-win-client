# Dependencies

## Dependency Policy

This project should remain lightweight and dependency-sparse.

Rules:

- Prefer platform/runtime features already included with .NET
- Add third-party packages only when they are clearly justified
- Record every added dependency here before or at the same time it is introduced
- Note why each dependency exists and whether it is required at runtime, build time, or only for development

## Confirmed Local Tooling

- .NET SDK 10.0.404 (stable .NET 10 feature bands and patches selected by `global.json`)
- Microsoft.WindowsDesktop.App 10.0.15 runtime
- Microsoft.NETCore.App 10.0.15 runtime

## Approved Baseline

Current project dependencies:

- .NET 10 SDK (LTS)
  - Purpose: build the application
  - Type: build/runtime platform
  - Status: available locally
- WPF
  - Purpose: native Windows desktop UI
  - Type: runtime platform
  - Status: included in installed Windows desktop runtime
- Internal core library (`WarpinatorWinClient.Core`)
  - Purpose: shared protocol contracts, settings models, peer models, and transfer models
  - Type: internal project dependency
  - Status: added
- Google.Protobuf 3.34.0
  - Purpose: protobuf message support for the official Warpinator schema
  - Type: runtime/build
  - Status: approved and added
- Grpc.Core 2.46.6
  - Purpose: desktop-friendly gRPC client/server runtime for Warpinator registration and compatibility hosting
  - Type: runtime
  - Status: approved and added
- Grpc.Tools 2.78.0
  - Purpose: regenerate C# RPC types from `warp.proto` when the checked-in protocol sources need to be refreshed
  - Type: build-time only
  - Status: approved and added
- Makaretu.Dns.Multicast 0.19.0
  - Purpose: advertise and browse `_warpinator._tcp.local.` peers over mDNS
  - Type: runtime
  - Status: approved and added
- Sodium.Core 1.4.0
  - Purpose: match Linux Warpinator's secretbox-based certificate exchange
  - Type: runtime
  - Status: approved and added

## Deferred or Rejected Dependencies

- Grpc.AspNetCore
  - Reason deferred: would require a heavier ASP.NET Core hosting path for a desktop app on the current local SDK/runtime setup
- Grpc.Net.Client
  - Reason deferred: `Grpc.Core` already covers the client and server path we need today with fewer moving pieces in this desktop app
- NaCl.Core
  - Reason removed from plan: a libsodium-compatible `SecretBox` implementation was the safer fit for Warpinator certificate exchange

## Current Assessment

Likely unavoidable external dependency areas:

- gRPC/protobuf support
- mDNS/zeroconf service discovery
- NaCl/libsodium-compatible secretbox encryption for group-code certificate exchange

Areas we expect to keep dependency-free:

- UI shell and view models
- local file system access
- transfer queue state
- application settings and logging
- basic project/domain models
- protocol defaults and compatibility contracts

## Change Log

- 2026-11-13: Added targeted duplex-timeout registration recovery using the existing gRPC registration service; dependency set unchanged
- 2026-11-13: Corrected receive completion for Linux's allocated-size metadata and added item-count and stream-error regression coverage without adding dependencies
- 2026-11-13: Fixed discovery retries and added the Search action, isolated regression checks, and a live discovery diagnostic using the existing .NET, WPF, and mDNS dependencies
- 2026-11-13: Standardized the app, core library, regression runner, and Windows CI on .NET 10 LTS without adding NuGet dependencies; added stable SDK selection in `global.json`
- 2026-11-13: Removed obsolete framework build outputs, intermediate files, and the old receive-fix publication from the workspace
- 2026-03-27: Created dependency tracking document with no external packages approved yet
- 2026-03-27: Scaffolded the WPF application and confirmed it builds successfully without adding any external NuGet packages
- 2026-03-27: Added the internal core project and kept the external NuGet dependency count at zero while capturing upstream protocol defaults and service contracts
- 2026-03-27: Approved and added gRPC, protobuf, mDNS, and libsodium-compatible packages to implement live discovery and registration against Linux Warpinator
- 2026-03-27: Switched the protobuf/gRPC build path to checked-in generated sources because the WPF temporary project build was not compiling `Protobuf` outputs reliably on the current local toolchain
- 2026-03-27: Kept the dependency budget unchanged while adding the first Linux-to-Windows inbound transfer path, save-folder writer, and transfer-history runtime state
- 2026-03-27: Kept the dependency budget unchanged while adding the first Windows-to-Linux outbound single-file send path and lightweight send action in the native WPF shell
- 2026-03-27: Kept the dependency budget unchanged while switching outbound transfer offers onto the maintained secure session and adding deeper transfer callback diagnostics
- 2026-03-27: Kept the dependency budget unchanged while redesigning the UI around a transfer stage, live progress telemetry, ETA readouts, and drag-and-drop sending
- 2026-03-27: Kept the dependency budget unchanged while shifting the shell to a futuristic product-facing style and adding Explorer reveal actions for completed received files
- 2026-03-27: Kept the dependency budget unchanged while refining the Explorer reveal affordance into a compact folder-style action in the activity list
- 2026-03-27: Removed disposable debug logs and generated build output folders from the workspace without changing the dependency set
- 2026-03-27: Added GitHub-ready repository files and a Windows build workflow without changing the application dependency set
