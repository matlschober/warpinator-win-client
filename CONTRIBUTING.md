# Contributing

Thanks for helping improve the project.

## Development Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on Windows. The repository's `global.json` selects a stable .NET 10 SDK.
2. Clone the repository.
3. Restore and build:

```powershell
dotnet restore .\WarpinatorWinClient.sln
dotnet build .\WarpinatorWinClient.sln
```

## Guidelines

- Keep the app lightweight and dependency-sparse.
- Prefer built-in .NET functionality over new packages whenever practical.
- Update `agents.md` and `dependencies.md` when project status or dependencies change.
- Avoid committing `bin`, `obj`, logs, or local machine-specific files.
- Keep UI changes intentional and restrained.

## Pull Requests

- Keep PRs focused and easy to review.
- Include a short change summary and testing notes.
- Mention LAN interop behavior if you touched discovery, registration, or transfer logic.

## Reporting Issues

When reporting bugs, include:

- Windows version
- .NET SDK/runtime version
- Linux Warpinator version if known
- Whether the problem affects discovery, registration, send, receive, or UI
- Relevant log excerpts or screenshots if available
