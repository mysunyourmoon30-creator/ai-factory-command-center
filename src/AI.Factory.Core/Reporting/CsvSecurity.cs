using System.Text;

namespace AI.Factory.Core.Reporting;

/// <summary>
/// Locked CSV export defenses (Master Scope V4, §13.1): escape embedded quotes, quote-wrap
/// values containing a comma or newline, and prefix formula-injection characters with a
/// single quote so a value like "=1+1" opens as inert text, not a formula, in a spreadsheet.
/// </summary>
public static class CsvSecurity
{
    private static readonly char[] FormulaInjectionPrefixes = ['=', '+', '-', '@'];

    public static string EscapeField(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && FormulaInjectionPrefixes.Contains(text[0]))
        {
            text = "'" + text;
        }

        var needsQuoting = text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        if (!needsQuoting) return text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    public static string WriteRow(IEnumerable<string?> fields) => string.Join(',', fields.Select(EscapeField));

    /// <summary>Prefixes a UTF-8 BOM so spreadsheet applications render non-ASCII content correctly.</summary>
    public static byte[] WriteCsv(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(WriteRow(header)).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(WriteRow(row)).Append("\r\n");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(builder.ToString());
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }
}
