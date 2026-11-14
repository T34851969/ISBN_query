// 表格文件读写与 ISBN 列处理。移植自 Python 版 app/excel_io.py：
// - 读统一走 ExcelDataReader（xlsx=SAX 流式、xls=BIFF 直解，性能对标 Python 版的 calamine 引擎）；
// - 写统一落到 output/ 目录并带时间戳，避免静默覆盖；
// - 全字符串语义读取，ISBN 不失真（数值单元格直接按整数格式化，比旧版 ".0$" 正则更根本）。

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using MiniExcelLibs;

namespace ISBNQuery.Core;

public static class TableIo
{
    public static readonly string[] IsbnColumnCandidates = { "ISBN", "标准号", "ISBN标准号" };

    static TableIo()
    {
        // 旧版 .xls（BIFF）常带 GBK/936 代码页编码，ExcelDataReader 需要 CodePages 提供程序
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ---- 读取 ----

    /// <summary>按后缀解析上传文件内容为 DataTable（ISBN 不失真）。仅支持 .xlsx/.xls/.csv。</summary>
    public static DataTable ReadTable(string fileName, byte[] content)
    {
        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".csv"))
            return ReadCsv(content);
        if (name.EndsWith(".xlsx") || name.EndsWith(".xls"))
        {
            using var reader = ExcelReaderFactory.CreateReader(new MemoryStream(content));
            var ds = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true },
            });
            return ds.Tables.Count > 0 ? ds.Tables[0] : new DataTable();
        }
        throw new NotSupportedException($"不支持的文件格式：{fileName}（仅支持 .xlsx/.xls/.csv）");
    }

    /// <summary>按候选列名识别 ISBN 列；未命中返回 null（单列文件由调用方兜底）。</summary>
    public static string? PickIsbnColumn(DataTable dt)
    {
        foreach (var col in IsbnColumnCandidates)
            if (dt.Columns.Contains(col))
                return col;
        return null;
    }

    /// <summary>
    /// 单元格规范化（对应 Python 版 transform_isbn_series + Excel 数值修正）：
    /// 去首尾空格/连字符/空格；数值单元格直接按整数格式化（不产生浮点尾差）；
    /// 字符串再兜底修掉 ".0$" 尾差。
    /// </summary>
    public static string NormalizeIsbn(object? value)
    {
        string s = value switch
        {
            null or DBNull => "",
            double d => FormatNumber(d),
            float f => FormatNumber(f),
            decimal m => FormatNumber(m),
            DateTime dtm => dtm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
        s = s.Trim().Replace("-", "").Replace(" ", "");
        s = Regex.Replace(s, @"\.0+$", "");
        return s;
    }

    private static string FormatNumber(double d) =>
        double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);

    private static string FormatNumber(decimal m) =>
        m == Math.Floor(m) ? ((long)m).ToString(CultureInfo.InvariantCulture)
                           : m.ToString(CultureInfo.InvariantCulture);

    private static bool IsBlank(object? v) =>
        v is null || v == DBNull.Value || (v is string s && s.Trim().Length == 0);

    /// <summary>
    /// 提取并清洗 ISBN 值（对应 Python 版 clean_isbn_values）：
    /// 去空值/表头残留行（"标准号"），规范化后剔除空串与 "nan"。
    /// </summary>
    public static List<string> CleanIsbnValues(DataTable dt, string col)
    {
        var list = new List<string>();
        foreach (DataRow row in dt.Rows)
        {
            var raw = row[col];
            if (IsBlank(raw))
                continue;
            if (Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() == "标准号")
                continue;
            var s = NormalizeIsbn(raw);
            if (s.Length == 0 || s.Equals("nan", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// 流式快路径：只读 ISBN 列（不经 DataTable），供大文件建库/更新使用。
    /// 返回（识别到的列名或 null，清洗后的值列表）；语义与 CleanIsbnValues 一致。
    /// </summary>
    public static (string? Column, List<string> Values) ReadIsbnColumn(string fileName, byte[] content)
    {
        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".csv"))
            return ReadIsbnColumnCsv(content);
        if (name.EndsWith(".xlsx") || name.EndsWith(".xls"))
            return ReadIsbnColumnExcel(content);
        throw new NotSupportedException($"不支持的文件格式：{fileName}（仅支持 .xlsx/.xls/.csv）");
    }

    private static (string? Column, List<string> Values) ReadIsbnColumnExcel(byte[] content)
    {
        using var reader = ExcelReaderFactory.CreateReader(new MemoryStream(content));
        if (!reader.Read())
            return (null, new List<string>());

        // 首行为表头：按候选列名定位；单列文件任意列名兜底
        int col = -1;
        if (reader.FieldCount == 1)
        {
            col = 0;
        }
        else
        {
            for (int c = 0; c < reader.FieldCount; c++)
            {
                var header = Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)?.Trim();
                if (header is not null && IsbnColumnCandidates.Contains(header))
                {
                    col = c;
                    break;
                }
            }
        }
        if (col < 0)
            return (null, new List<string>());
        var colName = Convert.ToString(reader.GetValue(col), CultureInfo.InvariantCulture)?.Trim();

        var values = new List<string>();
        while (reader.Read())
        {
            var raw = reader.GetValue(col);
            if (IsBlank(raw) || RawIsHeaderRemnant(raw))
                continue;
            var s = NormalizeIsbn(raw);
            if (s.Length == 0 || s.Equals("nan", StringComparison.OrdinalIgnoreCase))
                continue;
            values.Add(s);
        }
        return (colName, values);
    }

    private static (string? Column, List<string> Values) ReadIsbnColumnCsv(byte[] content)
    {
        var text = DecodeText(content);
        var records = ParseCsvRecords(text);
        if (records.Count == 0)
            return (null, new List<string>());

        var header = records[0];
        int col = -1;
        string? colName = null;
        if (header.Count == 1)
        {
            col = 0;
            colName = header[0]?.Trim();
        }
        else
        {
            for (int c = 0; c < header.Count; c++)
            {
                var h = header[c]?.Trim();
                if (h is not null && IsbnColumnCandidates.Contains(h))
                {
                    col = c;
                    colName = h;
                    break;
                }
            }
        }
        if (col < 0)
            return (null, new List<string>());

        var values = new List<string>();
        for (int r = 1; r < records.Count; r++)
        {
            var rec = records[r];
            var raw = col < rec.Count ? rec[col] : "";
            if (IsBlank(raw) || RawIsHeaderRemnant(raw))
                continue;
            var s = NormalizeIsbn(raw);
            if (s.Length == 0 || s.Equals("nan", StringComparison.OrdinalIgnoreCase))
                continue;
            values.Add(s);
        }
        return (colName, values);
    }

    private static bool RawIsHeaderRemnant(object? raw) =>
        Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() == "标准号";

    /// <summary>
    /// 解析一组上传文件，提取并清洗 ISBN，返回去重后的列表（对应 parse_isbns_from_files）。
    /// 无候选列时单列文件可用任意列名；格式不支持/缺列的文件记日志后跳过。
    /// 走流式快路径（不经 DataTable），30 万行级文件建库可秒级完成。
    /// </summary>
    public static List<string> ParseIsbnsFromFiles(IReadOnlyList<(string Name, byte[] Content)> files)
    {
        var collected = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (name, content) in files)
        {
            string? col;
            List<string> values;
            try
            {
                (col, values) = ReadIsbnColumn(name, content);
            }
            catch (NotSupportedException e)
            {
                RingLogger.Global.Append(e.Message);
                continue;
            }
            if (col is null)
            {
                RingLogger.Global.Append($"文件 {name} 缺少 ISBN/标准号/ISBN标准号 列，已跳过。");
                continue;
            }
            foreach (var isbn in values)
                if (seen.Add(isbn))
                    collected.Add(isbn);
        }
        return collected;
    }

    // ---- 批量清洗（整行过滤） ----

    /// <summary>
    /// 剔除库中已存在的行，返回保留行的整表副本（ISBN 列写回规范化值，与旧版一致）。
    /// 语义对齐旧版：先剔除空行与表头残留行，再逐格规范化，最后按规范化值比对。
    /// </summary>
    public static DataTable FilterExistingRows(DataTable dt, string col, HashSet<string> found)
    {
        var kept = dt.Clone();
        foreach (DataRow row in dt.Rows)
        {
            var raw = row[col];
            if (IsBlank(raw))
                continue;
            if (Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() == "标准号")
                continue;
            var norm = NormalizeIsbn(raw);
            row[col] = norm; // 导出的整行里 ISBN 列保持规范化后的值（同旧版 transform 写回）
            if (!found.Contains(norm))
                kept.ImportRow(row);
        }
        return kept;
    }

    // ---- 写出 ----

    /// <summary>把清洗结果整行写入 output/，文件名带时间戳，避免静默覆盖。</summary>
    public static string WriteExcel(DataTable rows, string sourceName, string? outputDir = null)
    {
        var dir = outputDir ?? AppPaths.OutputDir;
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileNameWithoutExtension(sourceName)}_cleaned_{stamp}.xlsx");
        MiniExcel.SaveAs(path, rows);
        return path;
    }

    /// <summary>把批量查询结果写入 output/（utf-8-sig，Windows Excel 打开不乱码）。</summary>
    public static string WriteResultCsv(IReadOnlyList<(string Isbn, string Result)> rows, string? outputDir = null)
    {
        var dir = outputDir ?? AppPaths.OutputDir;
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = System.IO.Path.Combine(dir, $"isbn_check_result_{stamp}.csv");
        var sb = new StringBuilder();
        sb.Append("isbn,result\n");
        foreach (var (isbn, result) in rows)
        {
            sb.Append(EscapeCsv(isbn)).Append(',').Append(EscapeCsv(result)).Append('\n');
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static string EscapeCsv(string? field)
    {
        field ??= "";
        bool needQuote = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (!needQuote)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    // ---- CSV 解析（RFC 4180：引号字段、双引号转义、CRLF/LF） ----

    private static DataTable ReadCsv(byte[] content)
    {
        var text = DecodeText(content);
        var records = ParseCsvRecords(text);
        var dt = new DataTable();
        if (records.Count == 0)
            return dt;

        // 表头行：空表头单元补列名，重复列名加后缀（与常见表格引擎行为一致）
        var header = records[0];
        var colNames = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var n = header[i]?.Trim();
            if (string.IsNullOrEmpty(n))
                n = $"Column{i + 1}";
            var final = n;
            int k = 2;
            while (!used.Add(final))
                final = $"{n}_{k++}";
            colNames.Add(final);
        }
        foreach (var n in colNames)
            dt.Columns.Add(n, typeof(string));

        for (int r = 1; r < records.Count; r++)
        {
            var rec = records[r];
            var cells = new object[colNames.Count];
            for (int c = 0; c < colNames.Count; c++)
                cells[c] = c < rec.Count ? rec[c] ?? "" : "";
            dt.Rows.Add(cells);
        }
        return dt;
    }

    /// <summary>UTF-8 严格解码失败时回退 GBK/936（办公场景常见），仍失败回退 Latin-1。</summary>
    private static string DecodeText(byte[] content)
    {
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return strict.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                RingLogger.Global.Append("CSV 非 UTF-8 编码，已按 GBK 解码。");
                return Encoding.GetEncoding(936).GetString(content);
            }
            catch
            {
                return Encoding.Latin1.GetString(content);
            }
        }
    }

    private static List<List<string>> ParseCsvRecords(string text)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            records.Add(row);
            row = new List<string>();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                EndRow();
            }
            else if (ch == '\n')
            {
                EndRow();
            }
            else
            {
                field.Append(ch);
            }
        }
        // 末尾无换行的最后一条（含逗号结尾的空字段）
        if (field.Length > 0 || row.Count > 0)
            EndRow();
        return records;
    }
}
