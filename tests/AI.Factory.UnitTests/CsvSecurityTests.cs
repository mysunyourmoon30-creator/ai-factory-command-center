using System.Text;
using AI.Factory.Core.Reporting;

namespace AI.Factory.UnitTests;

public sealed class CsvSecurityTests
{
    [Fact]
    public void Plain_value_is_not_escaped() =>
        Assert.Equal("RM-001", CsvSecurity.EscapeField("RM-001"));

    [Fact]
    public void Embedded_quotes_are_doubled_and_the_field_is_wrapped() =>
        Assert.Equal("\"Say \"\"hi\"\"\"", CsvSecurity.EscapeField("Say \"hi\""));

    [Theory]
    [InlineData("Acme, Inc.")]
    [InlineData("Line1\nLine2")]
    [InlineData("Line1\rLine2")]
    public void Comma_or_newline_forces_quote_wrapping(string value) =>
        Assert.StartsWith("\"", CsvSecurity.EscapeField(value));

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Formula_injection_prefixes_are_neutralized_with_a_leading_quote(string value, string expected) =>
        Assert.Equal(expected, CsvSecurity.EscapeField(value));

    [Fact]
    public void A_null_value_becomes_an_empty_field() =>
        Assert.Equal("", CsvSecurity.EscapeField(null));

    [Fact]
    public void Write_row_joins_escaped_fields_with_commas() =>
        Assert.Equal("A,\"B,C\",'=D", CsvSecurity.WriteRow(["A", "B,C", "=D"]));

    [Fact]
    public void Write_csv_starts_with_a_utf8_bom_and_crlf_line_endings()
    {
        var bytes = CsvSecurity.WriteCsv(["Col1", "Col2"], [["a", "b"]]);
        var preamble = Encoding.UTF8.GetPreamble();

        Assert.Equal(preamble, bytes[..preamble.Length]);
        var text = Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        Assert.Equal("Col1,Col2\r\na,b\r\n", text);
    }
}
