// 表格读取/写出测试：CSV 解析（RFC4180 + 编码回退）、xlsx 数值单元格、格式拒绝。

using System.Data;
using System.Text;
using ISBNQuery.Core;
using MiniExcelLibs;
using Xunit;

namespace ISBNQuery.Core.Tests;

public class TableIoTests : IDisposable
{
    private readonly string _dir;

    public TableIoTests()
    {
        // 本测试直接构造 GBK 字节，需先注册 CodePages（TableIo 静态构造可能尚未触发）
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _dir = Path.Combine(Path.GetTempPath(), "isbn-tableio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void CsvQuotedFieldsAndCrlf()
    {
        var csv = "ISBN,书名\r\n\"978-7-111,12345-6\",\"含\"\"引号\"\"的书\"\r\n9787000000002,普通\r\n";
        var dt = TableIo.ReadTable("a.csv", Encoding.UTF8.GetBytes(csv));
        Assert.Equal(2, dt.Rows.Count);
        Assert.Equal("978-7-111,12345-6", dt.Rows[0]["ISBN"]);
        Assert.Equal("含\"引号\"的书", dt.Rows[0]["书名"]);
        Assert.Equal("9787000000002", dt.Rows[1]["ISBN"]);
    }

    [Fact]
    public void CsvGbkFallback()
    {
        var bytes = Encoding.GetEncoding(936).GetBytes("ISBN,书名\n9787000000003,中文书名\n");
        var dt = TableIo.ReadTable("a.csv", bytes);
        Assert.Equal("中文书名", dt.Rows[0]["书名"]);
    }

    [Fact]
    public void CsvEmptyTrailingField()
    {
        // 逗号结尾：最后一个字段为空；末尾无换行也要成行
        var csv = "ISBN,书名\n9787000000004,\n";
        var dt = TableIo.ReadTable("a.csv", Encoding.UTF8.GetBytes(csv));
        Assert.Single(dt.Rows);
        Assert.Equal("", dt.Rows[0]["书名"]);
    }

    [Fact]
    public async Task XlsxNumericIsbnColumn()
    {
        var fixture = new DataTable();
        fixture.Columns.Add("ISBN", typeof(double));
        fixture.Columns.Add("书名", typeof(string));
        fixture.Rows.Add(9787111123458d, "n1");
        fixture.Rows.Add(9787030123459d, "n2");
        var path = Path.Combine(_dir, "f.xlsx");
        await MiniExcel.SaveAsAsync(path, fixture);

        var dt = TableIo.ReadTable("f.xlsx", File.ReadAllBytes(path));
        Assert.Equal(2, dt.Rows.Count);
        var list = TableIo.CleanIsbnValues(dt, "ISBN");
        Assert.Equal(new[] { "9787111123458", "9787030123459" }, list);
    }

    [Fact]
    public void UnsupportedFormatThrows()
    {
        Assert.Throws<NotSupportedException>(
            () => TableIo.ReadTable("a.txt", Encoding.UTF8.GetBytes("hello")));
    }

    [Fact]
    public void ParseIsbnsSingleColumnFallback()
    {
        var single = Encoding.UTF8.GetBytes("isbn_col\n9787111123456\n\n9787000000009\n");
        var parsed = TableIo.ParseIsbnsFromFiles([("single.csv", single)]);
        Assert.Equal(new[] { "9787111123456", "9787000000009" }, parsed);
    }

    [Fact]
    public void ParseIsbnsDeduplicatesAcrossFiles()
    {
        var a = Encoding.UTF8.GetBytes("ISBN\n9787111123456\n9787000000001\n");
        var b = Encoding.UTF8.GetBytes("标准号\n978-7-111-12345-6\n");
        var parsed = TableIo.ParseIsbnsFromFiles([("a.csv", a), ("b.csv", b)]);
        Assert.Equal(new[] { "9787111123456", "9787000000001" }, parsed);
    }

    [Fact]
    public async Task WriteExcelTimestampedName()
    {
        var dt = new DataTable();
        dt.Columns.Add("ISBN");
        dt.Columns.Add("书名");
        dt.Rows.Add("9787000000001", "x");
        var outDir = Path.Combine(_dir, "out");
        var path = TableIo.WriteExcel(dt, "货源.xlsx", outDir);
        Assert.StartsWith(Path.Combine(outDir, "货源_cleaned_"), path);
        Assert.EndsWith(".xlsx", path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteResultCsvHasBomAndHeader()
    {
        var outDir = Path.Combine(_dir, "out2");
        var path = TableIo.WriteResultCsv([("9787000000001", "存在"), ("9787000000002", "不存在")], outDir);
        var head = File.ReadAllBytes(path)[..3];
        Assert.Equal([0xEF, 0xBB, 0xBF], head); // utf-8-sig
        var text = File.ReadAllText(path);
        Assert.StartsWith("isbn,result\n9787000000001,存在\n9787000000002,不存在\n", text);
        await Task.CompletedTask;
    }
}
