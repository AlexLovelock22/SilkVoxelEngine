using System;
using System.IO;
using System.Text;

public static class VoxelLog
{
    private static readonly object _lock = new();
    private static StreamWriter _writer;
    private static bool _enabled = true;

    public static void Init(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        _writer = new StreamWriter(
            new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8
        )
        {
            AutoFlush = false
        };

        Write("=== VOXEL LOG START ===");
    }

    public static void Enable(bool enabled)
    {
        _enabled = enabled;
    }

    public static void Write(string message)
    {
        if (!_enabled || _writer == null)
            return;

        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
        }
    }

    public static void Flush()
    {
        lock (_lock)
        {
            _writer?.Flush();
        }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            Write("=== VOXEL LOG END ===");
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
