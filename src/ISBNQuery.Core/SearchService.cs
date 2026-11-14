// 查询引擎：纯同步函数，返回结果对象，由 UI 层放入工作线程执行。
// 移植自 Python 版 app/search.py：批量查询不逐条刷日志——日志只记摘要，
// 逐条结果通过返回值交给界面一次性渲染，根治"每条日志全量重渲染"的回调风暴。

using System.Data;

namespace ISBNQuery.Core;

public sealed class BatchResult
{
    /// <summary>批量查询结果：Items 保持输入顺序，元素为 (isbn, 是否存在)。</summary>
    public List<(string Isbn, bool Exists)> Items { get; init; } = new();

    public int Total => Items.Count;
    public int FoundCount => CountMatching(true);
    public int MissingCount => CountMatching(false);

    private int CountMatching(bool exists)
    {
        int n = 0;
        foreach (var (_, ok) in Items)
            if (ok == exists) n++;
        return n;
    }
}

public static class SearchService
{
    /// <summary>单次查询，返回是否在库；输入无效时返回 null。</summary>
    public static bool? SearchSingle(IsbnDatabase db, string? raw)
    {
        var isbn = TableIo.NormalizeIsbn(raw);
        if (isbn.Length == 0)
        {
            RingLogger.Global.Append("非法输入");
            return null;
        }
        bool exists = db.ExistsSingle(isbn);
        RingLogger.Global.Append($"{isbn} {(exists ? "存在" : "不存在")}");
        return exists;
    }

    /// <summary>批量查询：去重、清洗后整批比对，结果按输入顺序返回。</summary>
    public static BatchResult SearchBatch(IsbnDatabase db, IEnumerable<string?> rawLines)
    {
        var isbns = new List<string>();
        var seen = new HashSet<string>();
        foreach (var line in rawLines)
        {
            var isbn = TableIo.NormalizeIsbn(line);
            if (isbn.Length > 0 && seen.Add(isbn))
                isbns.Add(isbn);
        }
        if (isbns.Count == 0)
        {
            RingLogger.Global.Append("没有可用的输入。");
            return new BatchResult();
        }

        var found = db.FetchExisting(isbns);
        var result = new BatchResult { Items = isbns.Select(i => (i, found.Contains(i))).ToList() };
        RingLogger.Global.Append(
            $"批量查询完成：共 {result.Total} 条，存在 {result.FoundCount}，不存在 {result.MissingCount}");
        return result;
    }

    /// <summary>
    /// 批量清洗：剔除库中已存在的行，整行结果写入 output/，返回保留的 ISBN。
    /// 对应 Python 版 search.batch_clean。
    /// </summary>
    public static List<string> BatchClean(IsbnDatabase db,
        IReadOnlyList<(string Name, byte[] Content)> files,
        Action<double, string>? progress = null,
        string? outputDir = null)
    {
        var keptAll = new List<string>();
        for (int idx = 0; idx < files.Count; idx++)
        {
            var (name, content) = files[idx];
            progress?.Invoke(idx / (double)files.Count, $"读取 {name}");
            RingLogger.Global.Append($"读取 {name} 中...");

            DataTable dt;
            try
            {
                dt = TableIo.ReadTable(name, content);
            }
            catch (NotSupportedException e)
            {
                RingLogger.Global.Append(e.Message);
                continue;
            }
            var col = TableIo.PickIsbnColumn(dt);
            if (col is null)
            {
                RingLogger.Global.Append($"文件 {name} 缺少 ISBN/标准号/ISBN标准号 列。");
                continue;
            }

            // 与旧版一致：先剔除空行与表头行，再逐格规范化（保持行对齐供整行过滤导出）
            var values = new List<string>();
            foreach (DataRow row in dt.Rows)
            {
                var raw = row[col];
                if (raw is null || raw == DBNull.Value ||
                    (raw is string s && s.Trim().Length == 0) ||
                    Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)?.Trim() == "标准号")
                    continue;
                values.Add(TableIo.NormalizeIsbn(raw));
            }

            var found = db.FetchExisting(values);
            var kept = TableIo.FilterExistingRows(dt, col, found);

            progress?.Invoke((idx + 0.5) / files.Count, $"导出 {name} 的清洗结果");
            var outPath = TableIo.WriteExcel(kept, name, outputDir);
            keptAll.AddRange(TableIo.CleanIsbnValues(kept, col));
            RingLogger.Global.Append(
                $"{name} 完成：在库剔除 {found.Count} 行，保留 {kept.Rows.Count} 行 → {outPath}");
        }

        RingLogger.Global.Append($"清洗完成，共保留 {keptAll.Count} 条。");
        return keptAll;
    }
}
