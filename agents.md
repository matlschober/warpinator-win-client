# Warpinator Windows Client

## Project Description

This project aims to build a lightweight Windows 11 desktop application that can communicate with Linux Mint's Warpinator on the same local network and transfer files in both directions.

Primary goals:

- Discover compatible Warpinator peers on the LAN
- Authenticate and pair using the Warpinator-compatible workflow
- Send files and folders from Windows to Linux
- Receive files and folders from Linux to Windows
- Keep the implementation lightweight and dependency-sparse

## Current Architecture Direction

- UI: WPF desktop application
- Core: dependency-free .NET class library for protocol contracts and shared models
- Runtime: .NET 10 LTS
- Language: C#
- Strategy: implement only the minimum required pieces for Warpinator interoperability first, then expand carefully

## UI Direction

- Use a minimal, state-of-the-art Windows product UI
- Favor strong typography, calm spacing, and clear operational hierarchy
- Keep the surface lightweight: very little chrome, very few boxes, one accent color
- Preserve good UX by making status, peers, transfers, and next actions easy to scan
- Apply the `frontend-skill` standard as the UI evolves so the app stays intentional rather than template-like

## Protocol Snapshot

Current upstream compatibility facts captured in code:

- Default transfer port: 42000
- Default authentication port: 42001
- Supported API versions observed upstream: 1 and 2
- Transfer service methods captured for planning: 11
- Registration service methods captured for planning: 2
- Default group code in local settings model: Warpinator
- Service discovery type: `_warpinator._tcp.local.`

Why this direction:

- WPF is built into the installed Windows desktop runtime and keeps the app native and lightweight
- .NET 10 LTS provides a supported runtime while preserving the lightweight native desktop architecture
- C# gives us strong async/networking support with low implementation friction
- A separate core library gives us a clean place for protocol and transfer logic without bloating the UI layer

## Project Status

Current phase: bidirectional file transfer paths implemented; live FLAC receive and Windows-restart reconnection verified; Windows-to-Linux destination verification pending

Completed:

- Confirmed that a Windows client compatible with Warpinator is technically feasible
- Verified local development environment includes the .NET 10 SDK and Windows desktop runtime
- Chosen a lightweight native application direction with WPF
- Upgraded the app, core library, regression runner, and Windows CI to .NET 10 LTS with stable SDK selection in `global.json`; verified a Release build with zero warnings/errors, all isolated TLS/gRPC regression checks, and framework-dependent Windows x64 publishing
- Created living project documents in the repository root
- Scaffolded a WPF solution and application project
- Refined the shell UI toward a modern, minimal product-surface design with stronger hierarchy and subtle entrance motion
- Added a dependency-free core project for shared settings, peer models, transfer models, protocol defaults, and service interfaces
- Added the official `warp.proto` file and checked in generated Warpinator RPC types for a deterministic desktop build
- Implemented local Warpinator-compatible registration hosting on TCP 42001
- Implemented local secure Warp service hosting on TCP 42000 as a compatibility shell for remote peers
- Implemented mDNS advertisement and browsing for `_warpinator._tcp.local.`
- Tightened discovery filtering so only `_warpinator._tcp` services are treated as peers
- Implemented remote certificate request/decryption and manual `RegisterService` calls against discovered peers
- Updated the shell UI to surface live discovery and registration state directly in the workspace
- Fixed the interop build so the app now compiles and runs from a fresh binary with `0 warnings` and `0 errors`
- Verified in a live Windows runtime test that the app binds TCP 42000 and TCP 42001 and joins mDNS on UDP 5353
- Verified runtime logging is working and captures startup and discovery events
- Confirmed the app currently discovers its own `_warpinator._tcp.local.` advertisement
- Validated live discovery of a Linux Warpinator peer at `192.168.86.35`
- Validated successful certificate exchange and `RegisterService` completion against the Linux peer
- Implemented automatic acceptance of inbound Linux transfer requests for the first simple receive path
- Implemented secure `StartTransfer` client pull from Linux to Windows using the remote certificate captured during registration
- Implemented save-folder writes for incoming regular files and directories, with transfer history surfaced in the UI and runtime snapshot
- Fixed an early-duplex race so inbound Linux Warp probes are accepted even before local mDNS discovery has fully settled
- Implemented a maintained reciprocal secure Warp session back to Linux after registration, including duplex checks and periodic ping keepalive
- Verified a real Linux-to-Windows file transfer succeeds
- Added the first Windows-initiated single-file send path, including local file selection, outbound transfer metadata, and streamed chunk delivery to Linux receivers
- Routed outbound transfer offers over the maintained secure Warp session instead of a throwaway channel, and added callback-level runtime logging for Linux `StartTransfer`/cancel/stop diagnostics
- Added a calmer transfer-centric desktop UI with a dedicated send stage, live progress surface, throughput and ETA readouts, and file drag-and-drop support
- Removed the remaining dev-facing protocol scaffolding from the visible UI, shifted the shell toward a futuristic dark product style, and added `Reveal in Explorer` actions for completed received files
- Refined the Explorer reveal affordance from a text chip into a compact folder action modeled after modern download-shelf UI
- Removed disposable build outputs, debug logs, and generated `bin`/`obj` folders from the workspace so the project tree stays deployment-ready
- Added GitHub/community packaging files: `README.md`, `.gitignore`, `LICENSE`, `CONTRIBUTING.md`, `CHANGELOG.md`, and a Windows build workflow under `.github/workflows`
- Fixed missed startup discovery with repeated queries, automatic 10-second browsing, a working Search action, immediate UI updates for inbound probes, and preservation of connected peers during repeated browsing
- Verified live discovery, certificate exchange, duplex checks, and secure-session keepalive against Linux peer `madmax-Aspire-E5-774G` (`MadMaxII`) at `192.168.86.53`; added isolated discovery and reconnection regression checks and an optional `--discover` diagnostic
- Verified automatic discovery after reopening Linux Warpinator and exercised the Search button through Windows UI Automation: busy feedback, completion, and preservation of the live connection all pass; Release build and all eight isolated regression checks pass
- Removed obsolete framework build outputs, intermediate files, and the old receive-fix publication; the project uses only the .NET 10 baseline
- Fixed false receive failures caused by upstream allocated-size metadata: require a clean gRPC stream and all advertised files/directories, use actual payload bytes for completed progress, and cover the observed 1,295,651 / 1,298,432-byte mismatch in TLS/gRPC regression checks
- Added targeted registration recovery after Linux's return-connection timeout following a Windows restart; normal discovery still avoids manual registration, and regression checks cover the timeout/recovery path
- Verified the real FLAC receive completes with 1,295,651 payload bytes against 1,298,432 advertised bytes, with the same SHA256 as the earlier copy; verified automatic reconnection after restarting Windows without restarting Linux; all twelve regression checks pass and the Release build has zero warnings/errors

In progress:

- Verifying the first real Windows-to-Linux file send against the live Linux machine
- Verifying the polished consumer-facing shell, Explorer reveal action, and updated transfer affordances against live file movement

Next milestones:

1. Verify a real Windows-to-Linux file lands successfully on the configured Linux save side
2. Expand outbound sending from a single file to folders and multi-file selections
3. Expand from multi-file sequential sending into proper folder and queue UX
4. Add clearer receive approvals and optional transfer notifications
5. Add automated tests around certificate handling, peer parsing, and transfer metadata

## Scope Guardrails

- Prefer built-in .NET libraries over third-party packages whenever practical
- Add external packages only when they remove substantial protocol complexity
- Avoid heavy UI frameworks and web-based desktop shells
- Keep the codebase modular so protocol logic can be tested separately from the UI
- Keep UX quality high even while features are incomplete, so the shell remains a solid base rather than throwaway scaffolding

## Open Questions

- Whether Linux Warpinator accepts the current Windows-side compatibility shell as fully online before file transfer exists
- Whether the checked-in generated gRPC sources should remain the default build path or be replaced later by a cleaner automated generation flow
- Whether the selected mDNS library behaves reliably across multiple Windows network adapters in real-world LAN conditions
- Whether symbolic links and full metadata preservation should stay deferred until after the first stable transfer release

