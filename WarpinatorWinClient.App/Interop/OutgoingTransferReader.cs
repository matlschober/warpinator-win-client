using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;

namespace WarpinatorWinClient.App.Interop;

internal sealed class OutgoingTransferReader
{
    private const int RegularFileType = 1;
    private readonly string path;
    private readonly string relativePath;
    private readonly int blockSizeBytes;

    public OutgoingTransferReader(string path, string relativePath, int blockSizeBytes = 1024 * 1024)
    {
        this.path = path;
        this.relativePath = relativePath;
        this.blockSizeBytes = blockSizeBytes;
    }

    public async IAsyncEnumerable<FileChunk> ReadChunksAsync()
    {
        var info = new FileInfo(path);
        var lastWriteUtc = info.LastWriteTimeUtc;
        var mtime = (ulong)new DateTimeOffset(lastWriteUtc).ToUnixTimeSeconds();
        var mtimeUsec = (uint)((lastWriteUtc.Ticks % TimeSpan.TicksPerSecond) / 10L);
        var fileTime = new FileTime
        {
            Mtime = mtime,
            MtimeUsec = mtimeUsec
        };

        var buffer = new byte[blockSizeBytes];
        var firstChunk = true;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (bytesRead == 0)
            {
                yield break;
            }

            var byteString = ByteString.CopyFrom(buffer, 0, bytesRead);
            yield return new FileChunk
            {
                RelativePath = relativePath,
                FileType = RegularFileType,
                Chunk = byteString,
                FileMode = 0,
                Time = firstChunk ? fileTime : null
            };

            firstChunk = false;
        }
    }
}
