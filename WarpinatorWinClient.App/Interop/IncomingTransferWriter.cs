using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WarpinatorWinClient.App.Interop;

internal sealed class IncomingTransferWriter : IDisposable
{
    private const int DirectoryFileType = 2;
    private const int SymbolicLinkFileType = 3;

    private readonly string saveRootPath;
    private readonly HashSet<string> allowedTopLevelNames;

    private FileStream? currentStream;
    private string? currentRelativePath;
    private string? currentResolvedPath;
    private ulong currentModifiedTime;
    private uint currentModifiedTimeUsec;

    public string RevealPath { get; }

    public IncomingTransferWriter(string saveRootPath, IEnumerable<string> topLevelNames)
    {
        this.saveRootPath = Path.GetFullPath(saveRootPath);
        var topLevelNameList = topLevelNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        allowedTopLevelNames = new HashSet<string>(
            topLevelNameList,
            StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(this.saveRootPath);

        RevealPath = topLevelNameList.Length == 1
            ? Path.Combine(this.saveRootPath, topLevelNameList[0])
            : this.saveRootPath;
    }

    public void WriteChunk(FileChunk chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk.RelativePath))
        {
            throw new InvalidOperationException("The sender did not provide a relative path for the incoming file chunk.");
        }

        var resolvedPath = ResolvePath(chunk.RelativePath);
        if (!string.Equals(currentRelativePath, chunk.RelativePath, StringComparison.Ordinal))
        {
            CloseCurrentStream();
            currentRelativePath = chunk.RelativePath;
            currentResolvedPath = resolvedPath;
            currentModifiedTime = chunk.Time?.Mtime ?? 0;
            currentModifiedTimeUsec = chunk.Time?.MtimeUsec ?? 0;
        }

        if (chunk.FileType == DirectoryFileType)
        {
            Directory.CreateDirectory(resolvedPath);
            return;
        }

        if (chunk.FileType == SymbolicLinkFileType)
        {
            throw new NotSupportedException("Symbolic-link transfers are not supported yet on the Windows client.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        currentStream ??= new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (chunk.Chunk.Length > 0)
        {
            chunk.Chunk.WriteTo(currentStream);
        }
    }

    public void Complete()
    {
        CloseCurrentStream();
    }

    public void Dispose()
    {
        CloseCurrentStream();
    }

    private string ResolvePath(string relativePath)
    {
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var segments = normalizedRelativePath
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Incoming transfer path is empty.");
        }

        if (allowedTopLevelNames.Count > 0 && !allowedTopLevelNames.Contains(segments[0]))
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Incoming transfer path '{0}' is not rooted in an approved top-level item.",
                    relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(saveRootPath, normalizedRelativePath));
        if (!fullPath.StartsWith(saveRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Incoming transfer path resolves outside the configured save folder.");
        }

        return fullPath;
    }

    private void CloseCurrentStream()
    {
        if (currentStream is null)
        {
            currentRelativePath = null;
            currentResolvedPath = null;
            currentModifiedTime = 0;
            currentModifiedTimeUsec = 0;
            return;
        }

        currentStream.Dispose();
        currentStream = null;

        if (!string.IsNullOrWhiteSpace(currentResolvedPath) && currentModifiedTime > 0)
        {
            var modifiedAt = DateTimeOffset.FromUnixTimeSeconds((long)currentModifiedTime)
                .AddTicks(currentModifiedTimeUsec * 10L);
            File.SetLastWriteTimeUtc(currentResolvedPath, modifiedAt.UtcDateTime);
        }

        currentRelativePath = null;
        currentResolvedPath = null;
        currentModifiedTime = 0;
        currentModifiedTimeUsec = 0;
    }
}
