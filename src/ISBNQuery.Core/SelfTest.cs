// 无 UI 冒烟自检：Core 全链路（解析→建库→查询→更新→清洗→旧库兼容），
// 供 exe --selftest 与 CI 使用。PASS 退出码 0，FAIL 退出码 1。

using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using MiniExcelLibs;

namespace ISBNQuery.Core;

public static class SelfTest
{
    public static int Run(TextWriter? stdout = null)
    {
        stdout ??= Console.Out;
        var workDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "isbnquery-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workDir);
            RunChecks(workDir, stdout);
            stdout.WriteLine("SELFTEST PASS");
            return 0;
        }
        catch (Exception e)
        {
            stdout.WriteLine($"SELFTEST FAIL: {e}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 清理失败不影响结果 */ }
        }
    }

    private static void Assert(bool cond, string message)
    {
        if (!cond)
            throw new InvalidOperationException("断言失败: " + message);
    }

    private static void RunChecks(string dir, TextWriter log)
    {
        // ---- 1. CSV 解析与规范化 ----
        var csv = """
            ISBN,书名
            978-7-111-12345-6,book1
            978 7 111 12345 7,book2

            标准号,表头残留
            9787111123458.0,book3
            """;
        var csvBytes = Encoding.UTF8.GetBytes(csv);
        var dt = TableIo.ReadTable("a.csv", csvBytes);
        var isbns = TableIo.CleanIsbnValues(dt, "ISBN");
        Assert(isbns.Count == 3, $"CSV 清洗数量: {isbns.Count} != 3");
        Assert(isbns[0] == "9787111123456", $"连字符清洗: {isbns[0]}");
        Assert(isbns[1] == "9787111123457", $"空格清洗: {isbns[1]}");
        Assert(isbns[2] == "9787111123458", $"浮点尾差清洗: {isbns[2]}");
        log.WriteLine("[1] CSV 解析与规范化 OK");

        // ---- 2. xlsx 数值单元格（double 12.0 → "12"）----
        var fixture = new DataTable();
        fixture.Columns.Add("ISBN", typeof(double));
        fixture.Columns.Add("书名", typeof(string));
        fixture.Rows.Add(9787111123458d, "n1");
        fixture.Rows.Add(9787030123459d, "n2");
        var xlsxPath = System.IO.Path.Combine(dir, "fixture.xlsx");
        MiniExcel.SaveAs(xlsxPath, fixture);
        var dt2 = TableIo.ReadTable("fixture.xlsx", File.ReadAllBytes(xlsxPath));
        var isbns2 = TableIo.CleanIsbnValues(dt2, "ISBN");
        Assert(isbns2.Count == 2 && isbns2[0] == "9787111123458", $"数值单元格格式化: {string.Join(",", isbns2)}");
        log.WriteLine("[2] xlsx 数值单元格 OK");

        // ---- 3. 建库 → 单查 → 批查 ----
        var dbPath = System.IO.Path.Combine(dir, "test.db");
        using (var db = new IsbnDatabase(dbPath))
        {
            db.Create(isbns);
            Assert(db.HasTable(), "HasTable");
            Assert(db.ExistsSingle("9787111123456"), "存在判断");
            Assert(!db.ExistsSingle("9787000000001"), "不存在判断");

            var batch = SearchService.SearchBatch(db, new[] { "978-7-111-12345-6", "9787000000001", "9787111123456" });
            Assert(batch.Total == 2, $"去重后数量: {batch.Total} != 2");
            Assert(batch.FoundCount == 1 && batch.MissingCount == 1, "存在/不存在计数");
            Assert(batch.Items[0].Isbn == "9787111123456" && batch.Items[0].Exists, "顺序保持");

            // IN 分批（>500）
            var many = new List<string>();
            for (int i = 0; i < 1200; i++)
                many.Add($"0000000000{i:D4}");
            db.Create(many);
            var found = db.FetchExisting(many);
            Assert(found.Count == 1200, $"分批查询: {found.Count} != 1200");
            log.WriteLine("[3] 建库/单查/批查/分批 OK");
        }

        // ---- 4. 更新（Temp_ISBN + INSERT OR IGNORE）----
        using (var db = new IsbnDatabase(dbPath))
        {
            var added = db.Update(new List<string>
            {
                "9787111123456",          // 已存在
                "9787111123457",          // 已存在
                "9787309000001",          // 新
            });
            Assert(added == 1, $"新增计数: {added} != 1");
            Assert(db.ExistsSingle("9787309000001"), "更新后可查");
            log.WriteLine("[4] 更新入库 OK");
        }

        // ---- 5. 旧 schema 兼容（Python 版结构：ISBN_table + idx_isbn）----
        var legacyPath = System.IO.Path.Combine(dir, "legacy.db");
        var cs = new SqliteConnectionStringBuilder { DataSource = legacyPath, Pooling = false }.ToString();
        using (var legacy = new SqliteConnection(cs))
        {
            legacy.Open();
            using var cmd = legacy.CreateCommand();
            cmd.CommandText = "CREATE TABLE ISBN_table (ISBN TEXT); INSERT INTO ISBN_table VALUES ('9787040000001'); CREATE INDEX idx_isbn ON ISBN_table (ISBN);";
            cmd.ExecuteNonQuery();
        }
        using (var db = new IsbnDatabase())
        {
            db.Switch(legacyPath);
            Assert(db.HasTable(), "旧库 HasTable");
            Assert(db.ExistsSingle("9787040000001"), "旧库查询");
            log.WriteLine("[5] 旧 .db 兼容 OK");
        }

        // ---- 6. 批量清洗 round-trip ----
        string outPath;
        using (var db = new IsbnDatabase(dbPath))
        {
            var cleanFixture = new DataTable();
            cleanFixture.Columns.Add("ISBN", typeof(string));
            cleanFixture.Columns.Add("书名", typeof(string));
            cleanFixture.Rows.Add("978-7-111-12345-6", "在库");     // 库中已有 → 剔除
            cleanFixture.Rows.Add("9787000000002", "新1");
            cleanFixture.Rows.Add("9787000000003", "新2");
            var f = System.IO.Path.Combine(dir, "clean.xlsx");
            MiniExcel.SaveAs(f, cleanFixture);
            var outDir = System.IO.Path.Combine(dir, "out");
            var kept = SearchService.BatchClean(db,
                new List<(string, byte[])> { ("clean.xlsx", File.ReadAllBytes(f)) },
                outputDir: outDir);
            Assert(kept.Count == 2, $"清洗保留数: {kept.Count} != 2");
            var files = Directory.GetFiles(outDir, "*.xlsx");
            Assert(files.Length == 1, "清洗输出文件");
            outPath = files[0];
            var outDt = TableIo.ReadTable("out.xlsx", File.ReadAllBytes(outPath));
            Assert(outDt.Rows.Count == 2, $"输出行数: {outDt.Rows.Count} != 2");
            Assert(outDt.Rows[0]["ISBN"].ToString() == "9787000000002", "输出 ISBN 已规范化");
            Assert(outDt.Rows[0]["书名"].ToString() == "新1", "整行保留");
        }
        log.WriteLine($"[6] 批量清洗 round-trip OK → {outPath}");

        // ---- 7. 单列 CSV 兜底 ----
        var single = Encoding.UTF8.GetBytes("isbn_col\n9787111123456\n9787000000009\n");
        var dtSingle = TableIo.ReadTable("single.csv", single);
        Assert(TableIo.PickIsbnColumn(dtSingle) is null, "非候选列名应为 null");
        var parsed = TableIo.ParseIsbnsFromFiles(new List<(string, byte[])> { ("single.csv", single) });
        Assert(parsed.Count == 2, $"单列兜底: {parsed.Count} != 2");
        log.WriteLine("[7] 单列 CSV 兜底 OK");
    }
}
