using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MakoMaster;

// The worker receives managed JSON only; it never accesses game state.
internal sealed class CaptureSession
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    internal static readonly JsonSerializerOptions JsonOptions = new()
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private Exception? error;
    internal Exception? Error => Volatile.Read(ref error);
    internal string FilePath { get; }
    internal Task Completion { get; }
    internal int Frames { get; private set; }
    internal CaptureSession(string filePath, long segmentByteLimit = 64L * 1024 * 1024)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        // Open synchronously so permission / path errors surface before enabling.
        var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        Completion = Task.Run(async () =>
        {
            try
            {
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                var part = 1;
                long written = 0;
                try
                {
                    await foreach (var line in queue.Reader.ReadAllAsync())
                    {
                        var bytes = Encoding.UTF8.GetByteCount(line) + 2;
                        if (written > 0 && written + bytes > segmentByteLimit)
                        {
                            await writer.DisposeAsync();
                            var next = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + $"-part{++part:D4}.jsonl");
                            writer = new StreamWriter(new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                            written = 0;
                        }
                        await writer.WriteLineAsync(line); written += bytes;
                    }
                }
                finally { await writer.DisposeAsync(); }
            }
            catch (Exception ex) { Volatile.Write(ref error, ex); stream.Dispose(); }
        });
    }
    internal bool TryWrite(object record)
    {
        if (Error != null) return false;
        var json = JsonSerializer.Serialize(record, JsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(json) + 2;
        if (bytes > 4 * 1024 * 1024)
        { Volatile.Write(ref error, new IOException("单条开发日志超过 4 MiB，已暂停输入；此前记录保留。")); return false; }
        if (!queue.Writer.TryWrite(json)) return false;
        Frames++;
        return true;
    }
    internal void Complete() => queue.Writer.TryComplete();
}
