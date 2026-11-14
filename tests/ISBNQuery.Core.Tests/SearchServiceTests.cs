// 查询服务测试：单查/批查（去重保序）/批量清洗 round-trip。

using System.Data;
using System.Text;
using ISBNQuery.Core;
using MiniExcelLibs;
using Xunit;

namespace ISBNQuery.Core.Tests;

public class SearchServiceTests : IDisposable
{
    private readonly string _dir;

    public SearchServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "isbn-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SearchSingleNormalizesAndLogs()
    {
        var dbPath = Path.Combine(_dir, "a.db");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });

        Assert.True(SearchService.SearchSingle(db, "978-7-111-12345-6"));
        Assert.False(SearchService.SearchSingle(db, "9787000000001"));
        Assert.Null(SearchService.SearchSingle(db, "   "));
        Assert.Null(SearchService.SearchSingle(db, ""));
    }

    [Fact]
    public void SearchBatchDedupesKeepsOrderAndCounts()
    {
        var dbPath = Path.Combine(_dir, "b.db");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });

        var result = SearchService.SearchBatch(db,
            new[] { "978-7-111-12345-6", "9787000000001", "9787111123456", "978 7 111 12345 6" });

        Assert.Equal(2, result.Total);            // 重复项去重
        Assert.Equal("9787111123456", result.Items[0].Isbn);
        Assert.True(result.Items[0].Exists);
        Assert.False(result.Items[1].Exists);
        Assert.Equal(1, result.FoundCount);
        Assert.Equal(1, result.MissingCount);
    }

    [Fact]
    public void SearchBatchWhitespaceOnlyReturnsEmpty()
    {
        var dbPath = Path.Combine(_dir, "c.db");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });
        var result = SearchService.SearchBatch(db, new[] { "", "   ", "\n" });
        Assert.Empty(result.Items);
    }

    [Fact]
    public void SearchBatchKeepsNanLiteral()
    {
        // 对齐旧版语义：批量粘贴路径不过滤 "nan"（只有文件清洗路径过滤），按字面量查询
        var dbPath = Path.Combine(_dir, "c2.db");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });
        var result = SearchService.SearchBatch(db, new[] { "", "nan" });
        Assert.Single(result.Items);
        Assert.Equal("nan", result.Items[0].Isbn);
    }

    [Fact]
    public async Task BatchCleanRemovesExistingRowsWholeRow()
    {
        var dbPath = Path.Combine(_dir, "d.db");
        var outDir = Path.Combine(_dir, "out");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });

        var fixture = new DataTable();
        fixture.Columns.Add("ISBN", typeof(string));
        fixture.Columns.Add("书名", typeof(string));
        fixture.Rows.Add("978-7-111-12345-6", "在库");
        fixture.Rows.Add("9787000000002", "新1");
        fixture.Rows.Add("9787000000003", "新2");
        var fixturePath = Path.Combine(_dir, "clean.xlsx");
        await MiniExcel.SaveAsAsync(fixturePath, fixture);

        var kept = SearchService.BatchClean(db,
            new List<(string, byte[])> { ("clean.xlsx", File.ReadAllBytes(fixturePath)) },
            outputDir: outDir);

        Assert.Equal(2, kept.Count);
        var files = Directory.GetFiles(outDir, "*.xlsx");
        Assert.Single(files);
        Assert.Contains("clean_cleaned_", Path.GetFileName(files[0]));

        var outDt = TableIo.ReadTable("out.xlsx", File.ReadAllBytes(files[0]));
        Assert.Equal(2, outDt.Rows.Count);
        Assert.Equal("9787000000002", outDt.Rows[0]["ISBN"]);   // ISBN 列已规范化写回
        Assert.Equal("新1", outDt.Rows[0]["书名"]);              // 整行保留
    }

    [Fact]
    public void BatchCleanSkipsFilesWithoutIsbnColumn()
    {
        var dbPath = Path.Combine(_dir, "e.db");
        var outDir = Path.Combine(_dir, "out2");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });

        var bad = Encoding.UTF8.GetBytes("名称,作者\nx,y\n");
        var good = Encoding.UTF8.GetBytes("ISBN\n9787000000008\n");
        var kept = SearchService.BatchClean(db,
            new List<(string, byte[])> { ("bad.csv", bad), ("good.csv", good) },
            outputDir: outDir);

        Assert.Single(kept);
        Assert.Single(Directory.GetFiles(outDir, "*.xlsx"));
    }

    [Fact]
    public void BatchCleanRejectsUnsupportedFormat()
    {
        var dbPath = Path.Combine(_dir, "f.db");
        var outDir = Path.Combine(_dir, "out3");
        using var db = new IsbnDatabase(dbPath);
        db.Create(new List<string> { "9787111123456" });

        var kept = SearchService.BatchClean(db,
            new List<(string, byte[])> { ("x.txt", Encoding.UTF8.GetBytes("hello")) },
            outputDir: outDir);
        Assert.Empty(kept);
        Assert.Empty(Directory.Exists(outDir) ? Directory.GetFiles(outDir) : []);
    }
}
