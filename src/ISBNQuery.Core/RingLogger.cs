// 全局运行日志：有界环形缓冲 + 版本号脏标记。
// 移植自 Python 版 app/logger.py：不向 UI 推送回调，界面端用定时器轮询
// Snapshot()，版本号变化才刷新、未被裁剪时只追加新行，避免逐条全量重渲染。

using System.Text;

namespace ISBNQuery.Core;

public sealed class RingLogger
{
    public const int MaxLines = 2000;

    private readonly object _lock = new();
    private readonly string[] _buf = new string[MaxLines];
    private int _head;      // 下一个写入位置
    private int _count;     // 当前有效行数
    private long _version;
    private long _dropped;  // 因超出上限被裁剪的总行数

    public void Append(string text)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}";
        lock (_lock)
        {
            if (_count == MaxLines)
                _dropped++;
            _buf[_head] = line;
            _head = (_head + 1) % MaxLines;
            if (_count < MaxLines)
                _count++;
            _version++;
        }
    }

    /// <summary>返回（版本号, 累计裁剪行数, 当前行列表副本），调用方据此做增量刷新。</summary>
    public (long Version, long Dropped, string[] Lines) Snapshot()
    {
        lock (_lock)
        {
            var lines = new string[_count];
            for (int i = 0; i < _count; i++)
                lines[i] = _buf[(_head - _count + i + MaxLines) % MaxLines];
            return (_version, _dropped, lines);
        }
    }

    /// <summary>把当前全部日志写入文本文件（UTF-8）。</summary>
    public string Export(string path)
    {
        string[] lines;
        lock (_lock)
            lines = Snapshot().Lines;
        File.WriteAllText(path, string.Join("\n", lines) + (lines.Length > 0 ? "\n" : ""), new UTF8Encoding(false));
        return path;
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buf);
            _head = 0;
            _count = 0;
            _version++;
        }
    }

    /// <summary>全局单例（对应 Python 版 Global_logger）。</summary>
    public static RingLogger Global { get; } = new();
}
