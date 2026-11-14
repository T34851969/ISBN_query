// 规范化与列识别单元测试（对齐 Python 版清洗语义）。

using System.Data;
using ISBNQuery.Core;
using Xunit;

namespace ISBNQuery.Core.Tests;

public class NormalizationTests
{
    [Theory]
    [InlineData("978-7-111-12345-6", "9787111123456")]
    [InlineData("978 7 111 12345 7", "9787111123457")]
    [InlineData("9787111123458", "9787111123458")]
    [InlineData("  978-7-111-12345-6  ", "9787111123456")]
    [InlineData("9787111123458.0", "9787111123458")]       // Excel 浮点尾差（字符串形态）
    [InlineData("9787111123458.00", "9787111123458")]      // 多位尾差（旧版 \.0+$ 同样覆盖）
    [InlineData("", "")]
    public void StringNormalize(string raw, string expected)
    {
        Assert.Equal(expected, TableIo.NormalizeIsbn(raw));
    }

    [Fact]
    public void DoubleCellBecomesIntegerString()
    {
        // Excel 数值单元格经 calamine/ExcelDataReader 读出为 double
        Assert.Equal("9787111123458", TableIo.NormalizeIsbn(9787111123458d));
        Assert.Equal("12", TableIo.NormalizeIsbn(12.0d));
    }

    [Fact]
    public void NullAndDbnullAreEmpty()
    {
        Assert.Equal("", TableIo.NormalizeIsbn(null));
        Assert.Equal("", TableIo.NormalizeIsbn(DBNull.Value));
    }

    private static DataTable MakeTable(params object?[] values)
    {
        var dt = new DataTable();
        dt.Columns.Add("ISBN", typeof(object));
        foreach (var v in values)
            dt.Rows.Add(v);
        return dt;
    }

    [Fact]
    public void CleanRemovesBlankHeaderAndNan()
    {
        var dt = MakeTable("978-7-111-12345-6", DBNull.Value, null, "标准号", "nan", "NaN", "", "   ", "9787000000001");
        var list = TableIo.CleanIsbnValues(dt, "ISBN");
        Assert.Equal(new[] { "9787111123456", "9787000000001" }, list);
    }

    [Theory]
    [InlineData("ISBN")]
    [InlineData("标准号")]
    [InlineData("ISBN标准号")]
    public void CandidateColumnsArePicked(string col)
    {
        var dt = new DataTable();
        dt.Columns.Add(col);
        Assert.Equal(col, TableIo.PickIsbnColumn(dt));
    }

    [Fact]
    public void UnknownColumnReturnsNull()
    {
        var dt = new DataTable();
        dt.Columns.Add("书号");
        dt.Columns.Add("名称");
        Assert.Null(TableIo.PickIsbnColumn(dt));
    }
}
