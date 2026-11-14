// 数据库层测试：建库/查询/分批/更新/旧 .db 兼容（Python 版 ISBN_table + idx_isbn）。

using System.Text;
using ISBNQuery.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ISBNQuery.Core.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dir;

    public DatabaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "isbn-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string DbPath(string name) => Path.Combine(_dir, name);

    [Fact]
    public void CreateAndQuery()
    {
        var path = DbPath("a.db");
        using var db = new IsbnDatabase(path);
        db.Create(new List<string> { "9787111123456", "9787111123457" });

        Assert.True(db.HasTable());
        Assert.True(db.ExistsSingle("9787111123456"));
        Assert.False(db.ExistsSingle("9787000000001"));

        var found = db.FetchExisting(new List<string> { "9787111123456", "9787000000001" });
        Assert.Single(found);
        Assert.Contains("9787111123456", found);
    }

    [Fact]
    public void CreateReplacesExistingTable()
    {
        var path = DbPath("b.db");
        using var db = new IsbnDatabase(path);
        db.Create(new List<string> { "9787111123456" });
        db.Create(new List<string> { "9787000000002" });
        Assert.False(db.ExistsSingle("9787111123456"));
        Assert.True(db.ExistsSingle("9787000000002"));
    }

    [Fact]
    public void FetchExistingBatchesOver500()
    {
        var path = DbPath("c.db");
        using var db = new IsbnDatabase(path);
        var isbns = Enumerable.Range(0, 1200).Select(i => $"9787111{i:D7}").ToList();
        db.Create(isbns);
        var found = db.FetchExisting(isbns);
        Assert.Equal(1200, found.Count);
    }

    [Fact]
    public void UpdateCountsOnlyNewRows()
    {
        var path = DbPath("d.db");
        using var db = new IsbnDatabase(path);
        db.Create(new List<string> { "9787111123456", "9787111123457" });

        var added = db.Update(new List<string> { "9787111123456", "9787111123457", "9787309000001", "9787309000002" });
        Assert.Equal(2, added);
        Assert.True(db.ExistsSingle("9787309000002"));
    }

    [Fact]
    public void UpdateWithoutTableThrows()
    {
        var path = DbPath("empty.db");
        using var db = new IsbnDatabase(path);
        Assert.Throws<InvalidOperationException>(() => db.Update(new List<string> { "9787111123456" }));
    }

    [Fact]
    public void LegacyPythonSchemaIsCompatible()
    {
        // 用裸 SQL 按 Python 旧版结构建库：ISBN_table(ISBN TEXT) + idx_isbn
        var path = DbPath("legacy.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE ISBN_table (ISBN TEXT);
                INSERT INTO ISBN_table VALUES ('9787040000001');
                INSERT INTO ISBN_table VALUES ('9787040000002');
                CREATE INDEX idx_isbn ON ISBN_table (ISBN);
                """;
            cmd.ExecuteNonQuery();
        }

        using var db = new IsbnDatabase();
        db.Switch(path);
        Assert.True(db.HasTable());
        Assert.True(db.ExistsSingle("9787040000001"));
        // 旧库直接增量更新也必须可用
        Assert.Equal(1, db.Update(new List<string> { "9787040000003" }));
    }

    [Fact]
    public void SwitchReopensConnection()
    {
        using var db = new IsbnDatabase(DbPath("e1.db"));
        db.Create(new List<string> { "9787111123456" });

        var other = DbPath("e2.db");
        db.Switch(other);
        db.Create(new List<string> { "9787000000009" });

        Assert.True(db.ExistsSingle("9787000000009"));
        Assert.False(db.ExistsSingle("9787111123456"));
        Assert.Equal(other, db.Path);
    }

    [Fact]
    public void ProgressIsReportedAndClamped()
    {
        var path = DbPath("p.db");
        using var db = new IsbnDatabase(path);
        var isbns = Enumerable.Range(0, 50_001).Select(i => $"9787111{i:D7}").ToList();
        var reports = new List<(double, string)>();
        db.Create(isbns, progress: (f, label) => reports.Add((f, label)));
        Assert.True(reports.Count > 0);
        Assert.All(reports, r => Assert.InRange(r.Item1, 0.0, 1.0));
        Assert.Equal(1.0, reports[^1].Item1, 3);
    }
}
