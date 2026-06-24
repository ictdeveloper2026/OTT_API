using System.Globalization;
using System.Text;

namespace OTT.API.Reporting;

/// <summary>
/// Minimal, allocation-light CSV writer (RFC 4180 quoting). Avoids a dependency for the
/// handful of admin report exports.
/// </summary>
public static class Csv
{
    /// <summary>Builds a UTF-8 CSV (with BOM, so Excel detects encoding) from a header + rows.</summary>
    public static byte[] Build(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(Format).Select(Escape)));

        // UTF-8 BOM keeps non-ASCII (titles, names) intact when opened in Excel.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
