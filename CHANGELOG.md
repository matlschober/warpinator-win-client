# Changelog

## Unreleased

- Require approval for incoming transfers by default, with sender/file/destination details and Accept/Decline actions; closing or cancelling the dialog declines without receiving files
- Add Settings with a persistent Yes/No receive-approval preference; automatic receiving applies only to new requests
- Handle duplicate, retried, queued, and withdrawn receive requests; add settings persistence and receive-approval TLS/gRPC regression coverage
- Give transfer titles, status badges, reveal actions, and progress labels dedicated space; refine progress bars and scroll controls and keep activity accessible at smaller window sizes
- Recover Linux's stale return connection after a Windows restart by refreshing registration only after the specific duplex timeout
- Fix false receive failures when Linux advertises allocated disk space instead of exact file bytes; validate clean stream completion and item counts, and finish progress at the actual received size
- Add regression coverage for the reported FLAC size mismatch, allocation overhead, missing items, and interrupted streams
- Remove obsolete framework build outputs, intermediate files, and the old receive-fix publication
- Retry LAN discovery automatically and add a responsive Search button with live search and connection status
- Show inbound peers immediately and preserve secure connections during repeated discovery
- Add discovery regression checks and an optional `--discover` live Linux connection diagnostic
- Upgrade the app, core library, regression runner, and Windows CI to .NET 10 LTS; select stable .NET 10 SDKs with `global.json`
- Avoid manual registration during mDNS discovery to prevent competing Linux peer records and missing transfer operations
- Decline receive compression until decompression is supported, and reject empty, interrupted, or missing-item receive streams
- Add isolated TLS/gRPC regression checks for peer registration, received content, and stream completion
- Initial public-ready repository setup
- Windows/Linux Warpinator-compatible discovery and registration
- Stable secure peer session maintenance
- Bidirectional single-file transfer support
- Transfer progress, ETA, drag-and-drop send, and Explorer reveal UI
- GitHub-ready documentation and Windows build workflow
