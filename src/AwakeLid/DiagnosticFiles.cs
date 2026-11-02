using System.Text;

namespace AwakeLid;

// Diagnostic output is disposable; it must never interrupt policy changes or recovery.
// The durable state.json journal deliberately does NOT use this class.
internal sealed class DiagnosticFiles(string directory, TimeProvider? timeProvider = null)
{
    const int MaxPendingLogs = 128;
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    readonly object gate = new();
    readonly Queue<(string File, string Text)> pendingLogs = new();
    string? publishedSnapshot;
    DateTimeOffset nextStatusAttempt, nextLogAttempt;

    public string StatusError { get; private set; } = "";
    public string LogError { get; private set; } = "";
    public int PendingLogEntries { get { lock (gate) return pendingLogs.Count; } }
    public long DroppedLogEntries { get; private set; }
    public string Warning
    {
        get
        {
            lock (gate) return string.Join("\r\n", new[] {
                StatusError.Length == 0 ? "" : "状态文件暂不可写，将自动重试：" + StatusError,
                LogError.Length == 0 ? "" : "日志暂不可写，将自动重试：" + LogError,
                DroppedLogEntries == 0 ? "" : $"日志缓冲已满，丢弃了 {DroppedLogEntries} 条旧诊断记录。"
            }.Where(message => message.Length != 0));
        }
    }

    public bool PublishStatus(string snapshot)
    {
        lock (gate) return PublishStatusCore(snapshot);
    }
    bool PublishStatusCore(string snapshot)
    {
        // Avoid rewriting identical snapshots every 500 ms, especially in synced folders.
        if (snapshot == publishedSnapshot && StatusError.Length == 0) return true;
        if (clock.GetUtcNow() < nextStatusAttempt) return false;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "status.json");
            // Reuse one staging file even if a reader prevents replacement for a long time.
            // Never truncate the last published snapshot in place.
            using (var stream = new FileStream(path + ".new", FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(snapshot);
            // ReplaceFile supports existing readers that allow deletion sharing;
            // MoveFileEx replacement can fail even with those readers on Windows.
            if (File.Exists(path)) File.Replace(path + ".new", path, null);
            else File.Move(path + ".new", path);
            publishedSnapshot = snapshot;
            StatusError = "";
            nextStatusAttempt = default;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            StatusError = error.Message;
            nextStatusAttempt = clock.GetUtcNow() + RetryDelay;
            return false;
        }
    }

    public void Log(string message, bool error = false)
    {
        lock (gate)
        {
            if (pendingLogs.Count == MaxPendingLogs)
            {
                pendingLogs.Dequeue();
                DroppedLogEntries++;
            }
            pendingLogs.Enqueue((error ? "error.log" : "events.log", $"{clock.GetLocalNow():O} {message}{Environment.NewLine}"));
            FlushLogsCore();
        }
    }

    public void FlushLogs()
    {
        lock (gate) FlushLogsCore();
    }
    void FlushLogsCore()
    {
        if (clock.GetUtcNow() < nextLogAttempt) return;
        try
        {
            while (pendingLogs.TryPeek(out var entry))
            {
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(Path.Combine(directory, entry.File), FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(entry.Text);
                pendingLogs.Dequeue();
            }
            LogError = "";
            nextLogAttempt = default;
        }
        catch (Exception error) when (IsFileError(error))
        {
            LogError = error.Message;
            nextLogAttempt = clock.GetUtcNow() + RetryDelay;
        }
    }

    static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
