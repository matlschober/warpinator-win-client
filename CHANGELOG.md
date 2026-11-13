# Changelog

## Unreleased

- Avoid manual registration during mDNS discovery to prevent competing Linux peer records and missing transfer operations
- Decline receive compression until decompression is supported, and reject incomplete receive streams
- Add isolated TLS/gRPC regression checks for peer registration, received content, and stream completion
- Initial public-ready repository setup
- Windows/Linux Warpinator-compatible discovery and registration
- Stable secure peer session maintenance
- Bidirectional single-file transfer support
- Transfer progress, ETA, drag-and-drop send, and Explorer reveal UI
- GitHub-ready documentation and Windows build workflow
