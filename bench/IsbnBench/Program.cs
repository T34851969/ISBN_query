// 性能基准：30 万行 xlsx 解析 / 建库 / 万条批量查询，对比 Python 版实测基准
// （解析 1.35s / 建库 0.5s / 万条查询 0.02s）。运行：dotnet run -c Release --project bench/IsbnBench

using System.Diagnostics;
using System.Data;
using ISBNQuery.Core;
using MiniExcelLibs;

var workDir = Path.Combine(Path.GetTempPath(), "isbn-bench-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDir);

try
{
    Console.WriteLine("== 生成 30 万行 xlsx fixture（生成时间不计入基准） ==");
    var fixture = new DataTable();
    fixture.Columns.Add("ISBN", typeof(double));
    fixture.Columns.Add("书名", typeof(string));
    for (int i = 0; i < 300_000; i++)
        fixture.Rows.Add(9787000000000d + i, $"书名{i}");
    var fixturePath = Path.Combine(workDir, "big.xlsx");
    MiniExcel.SaveAs(fixturePath, fixture);
    Console.WriteLine($"fixture: {fixturePath} ({new FileInfo(fixturePath).Length / 1024 / 1024} MB)");

    var sw = Stopwatch.StartNew();

    // ---- 1. 解析 30 万行（走建库同款快路径） ----
    var content = File.ReadAllBytes(fixturePath);
    var t1 = Stopwatch.StartNew();
    var (col, isbns) = TableIo.ReadIsbnColumn("big.xlsx", content);
    t1.Stop();
    Console.WriteLine($"[解析] 30 万行 xlsx → {isbns.Count} 条 ISBN（列 {col}）：{t1.ElapsedMilliseconds} ms（Python 基准 1350 ms）");
    sw.Restart();

    // ---- 2. 建库 ----
    var dbPath = Path.Combine(workDir, "bench.db");
    var t2 = Stopwatch.StartNew();
    using (var db = new IsbnDatabase(dbPath))
    {
        db.Create(isbns);
    }
    t2.Stop();
    Console.WriteLine($"[建库] {isbns.Count} 条入库：{t2.ElapsedMilliseconds} ms（Python 基准 500 ms）");

    // ---- 3. 万条批量查询（一半在库） ----
    var query = new List<string>(10_000);
    for (int i = 0; i < 10_000; i++)
    {
        query.Add(i % 2 == 0
            ? (9787000000000L + i / 2).ToString()   // 在库（fixture 前 5000 条）
            : (9990000000000L + i).ToString());     // 不在库
    }
    using (var db = new IsbnDatabase(dbPath))
    {
        var t3 = Stopwatch.StartNew();
        var found = db.FetchExisting(query);
        t3.Stop();
        Console.WriteLine($"[查询] 1 万条批量查询（命中 {found.Count}）：{t3.ElapsedMilliseconds} ms（Python 基准 20 ms）");
    }

    Console.WriteLine($"工作目录（已可清理）：{workDir}");
}
finally
{
    try { Directory.Delete(workDir, recursive: true); } catch { }
}
