// SQLite 数据库管理。移植自 Python 版 app/database.py，核心约定一致：
// - 持有一个长连接（Pooling=false），所有访问经 _lock 串行化；
// - 建库/更新按 InsertChunk 分块写入并回报进度，大文件入库不一次性阻塞；
// - 表结构保持 ISBN_table(ISBN TEXT) + idx_isbn，与旧 Python 版生成的 .db 完全兼容。
// 阻塞式方法只允许在工作线程中调用（ExistsSingle/HasTable 短暂持锁，事件线程也可调用）。

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ISBNQuery.Core;

public sealed class IsbnDatabase : IDisposable
{
    public const int QueryBatch = 500;      // IN 查询每批参数数（留足 SQLite 变量数上限余量）
    public const int InsertChunk = 50_000;  // 入库分块行数，兼顾内存与进度粒度
    public const string TableName = "ISBN_table";
    public const string IndexName = "idx_isbn";

    private readonly object _lock = new();
    private SqliteConnection? _conn;

    public string Path { get; private set; }

    public IsbnDatabase(string? path = null)
    {
        Path = string.IsNullOrWhiteSpace(path) ? DefaultDbPath : path;
        _conn = Open(Path);
    }

    public static string DefaultDbPath => System.IO.Path.Combine(AppPaths.DataDir, "ISBN.db");

    // ---- 连接管理 ----

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false, // 长连接模式：连接与页缓存跨操作复用
        }.ToString());
        conn.Open();
        ExecutePragma(conn, "PRAGMA cache_size = -65536");  // 64MB 页缓存
        ExecutePragma(conn, "PRAGMA temp_store = MEMORY");
        return conn;
    }

    private static void ExecutePragma(SqliteConnection conn, string pragma)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = pragma;
        cmd.ExecuteNonQuery();
    }

    public void Switch(string path)
    {
        lock (_lock)
        {
            _conn?.Dispose();
            Path = path;
            _conn = Open(path);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _conn?.Dispose();
            _conn = null;
        }
    }

    public bool IsExist() => File.Exists(Path);

    public bool HasTable()
    {
        lock (_lock)
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{TableName}'";
            return cmd.ExecuteScalar() is not null;
        }
    }

    // ---- 查询 ----

    public bool ExistsSingle(string isbn)
    {
        lock (_lock)
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {TableName} WHERE ISBN = @p0 LIMIT 1";
            cmd.Parameters.AddWithValue("@p0", isbn);
            return cmd.ExecuteScalar() is not null;
        }
    }

    /// <summary>返回给定 ISBN 列表中已存在于库内的集合。</summary>
    public HashSet<string> FetchExisting(IReadOnlyList<string> isbns)
    {
        var found = new HashSet<string>();
        lock (_lock)
        {
            for (int i = 0; i < isbns.Count; i += QueryBatch)
            {
                int n = Math.Min(QueryBatch, isbns.Count - i);
                var sb = new StringBuilder("SELECT ISBN FROM ").Append(TableName).Append(" WHERE ISBN IN (");
                for (int j = 0; j < n; j++)
                    sb.Append(j == 0 ? "@p" : ",@p").Append(j);
                sb.Append(')');

                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = sb.ToString();
                for (int j = 0; j < n; j++)
                    cmd.Parameters.AddWithValue("@p" + j, isbns[i + j]);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    found.Add(reader.GetString(0));
            }
        }
        return found;
    }

    // ---- 建库 / 更新 ----

    /// <summary>以给定 ISBN 列表重建 ISBN_table（原表内容被替换），语义同旧版 to_sql(replace)。</summary>
    public void Create(IReadOnlyList<string> isbns, string? path = null, Action<double, string>? progress = null)
    {
        if (!string.IsNullOrEmpty(path) && path != Path)
            Switch(path);
        int total = isbns.Count;
        lock (_lock)
        {
            Execute($"DROP TABLE IF EXISTS {TableName}");
            Execute($"CREATE TABLE {TableName} (ISBN TEXT)");
            for (int i = 0; i < total; i += InsertChunk)
            {
                int n = Math.Min(InsertChunk, total - i);
                using var tx = _conn!.BeginTransaction();
                InsertRows(tx, TableName, isbns, i, n);
                tx.Commit();
                Report(progress, (i + n) / (double)total, $"写入 {i + n}/{total}");
            }
            Execute($"CREATE INDEX IF NOT EXISTS {IndexName} ON {TableName} (ISBN)");
        }
    }

    /// <summary>
    /// 把 ISBN 列表并入当前库，返回实际新增行数。
    /// 注意：旧表 ISBN_table 无 UNIQUE 约束，纯 INSERT OR IGNORE 并不去重——旧 Python 版
    /// 此处实际会全量插入重复行（与其 README"自动去重"承诺不符）。本版改为先查库剔除
    /// 已有行再插入：表结构保持不变（旧 .db 兼容），语义兑现"实际新增条数"。
    /// </summary>
    public int Update(IReadOnlyList<string> isbns, Action<double, string>? progress = null)
    {
        if (!HasTable())
            throw new InvalidOperationException($"数据库 {Path} 尚未初始化，请先创建数据库。");

        // 输入自身去重（保序），再剔除库中已有行
        var seen = new HashSet<string>();
        var pending = new List<string>();
        foreach (var isbn in isbns)
            if (!string.IsNullOrEmpty(isbn) && seen.Add(isbn))
                pending.Add(isbn);
        var existing = FetchExisting(pending);
        var newRows = pending.Where(x => !existing.Contains(x)).ToList();

        int total = newRows.Count;
        int added;
        lock (_lock)
        {
            Execute("DROP TABLE IF EXISTS Temp_ISBN");
            Execute("CREATE TABLE Temp_ISBN (ISBN TEXT)");
            for (int i = 0; i < total; i += InsertChunk)
            {
                int n = Math.Min(InsertChunk, total - i);
                using (var tx = _conn!.BeginTransaction())
                {
                    InsertRows(tx, "Temp_ISBN", newRows, i, n);
                    tx.Commit();
                }
                Report(progress, (i + n) / (double)total * 0.9, $"解析待并入 {i + n}/{total}");
            }
            Execute("CREATE INDEX IF NOT EXISTS idx_temp_isbn ON Temp_ISBN (ISBN)");
            added = Execute("INSERT OR IGNORE INTO " + TableName + " (ISBN) SELECT ISBN FROM Temp_ISBN");
            Execute("DROP TABLE Temp_ISBN");
        }
        return added;
    }

    // ---- 内部 ----

    private int Execute(string sql)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    private void InsertRows(SqliteTransaction tx, string table, IReadOnlyList<string> isbns, int start, int count)
    {
        // 常量占位符模板 + 参数复用：同一条 SQL 反复执行，避免 SQLite 反复解析
        // 不同行数的多值语句（每批语句文本若不同，会各自触发一次解析与执行计划）
        const int K = 128;
        var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        var sb = new StringBuilder("INSERT INTO ").Append(table).Append(" (ISBN) VALUES ");
        for (int j = 0; j < K; j++)
        {
            if (j > 0) sb.Append(',');
            sb.Append("(@p").Append(j).Append(')');
        }
        cmd.CommandText = sb.ToString();
        var ps = new SqliteParameter[K];
        for (int j = 0; j < K; j++)
            ps[j] = cmd.Parameters.AddWithValue("@p" + j, "");

        int done = 0;
        while (done < count)
        {
            int n = Math.Min(K, count - done);
            if (n == K)
            {
                for (int j = 0; j < n; j++)
                    ps[j].Value = isbns[start + done + j] ?? "";
                cmd.ExecuteNonQuery();
            }
            else
            {
                // 尾批：每 chunk 至多一次，单独构造
                using var tail = _conn.CreateCommand();
                tail.Transaction = tx;
                var tsb = new StringBuilder("INSERT INTO ").Append(table).Append(" (ISBN) VALUES ");
                for (int j = 0; j < n; j++)
                {
                    if (j > 0) tsb.Append(',');
                    tsb.Append("(@q").Append(j).Append(')');
                }
                tail.CommandText = tsb.ToString();
                for (int j = 0; j < n; j++)
                    tail.Parameters.AddWithValue("@q" + j, isbns[start + done + j] ?? "");
                tail.ExecuteNonQuery();
            }
            done += n;
        }
        cmd.Dispose();
    }

    private static void Report(Action<double, string>? progress, double fraction, string label)
    {
        if (progress is null)
            return;
        try
        {
            progress(Math.Clamp(fraction, 0.0, 1.0), label);
        }
        catch
        {
            // 进度回调异常不中断任务（同旧版 _report 吞异常）
        }
    }
}
