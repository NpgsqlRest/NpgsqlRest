using System.IO.Pipelines;
using System.Text;
using Npgsql;

namespace NpgsqlRest.TableFormatHandlers;

public class HtmlTableFormatHandler : ITableFormatHandler
{
    /// <summary>
    /// Content written before the table. Typically a CSS style block.
    /// Set to null to omit.
    /// </summary>
    public string? Header { get; set; } =
        "<style>table{font-family:Calibri,Arial,sans-serif;font-size:11pt;border-collapse:collapse}" +
        "th,td{border:1px solid #d4d4d4;padding:4px 8px}" +
        "th{background-color:#f5f5f5;font-weight:600}</style>";

    /// <summary>
    /// Content written after the closing table tag.
    /// Set to null to omit.
    /// </summary>
    public string? Footer { get; set; } = null;

    public string ContentType => "text/html; charset=utf-8";

    public async Task RenderAsync(
        NpgsqlDataReader reader,
        Routine routine,
        RoutineEndpoint endpoint,
        PipeWriter writer,
        HttpContext context,
        ulong bufferRows,
        Dictionary<string, string>? customParameters,
        CancellationToken cancellationToken)
    {
        var columnCount = routine.ColumnCount;
        var descriptors = routine.ColumnsTypeDescriptor;
        var rowBuilder = StringBuilderPool.Rent(512);
        try
        {
            // Header
            if (Header is not null)
            {
                rowBuilder.Append(Header);
            }

            // Open table and write column headers
            rowBuilder.Append("<table><tr>");
            for (int i = 0; i < columnCount; i++)
            {
                rowBuilder.Append("<th>");
                HtmlEncodeAppend(rowBuilder, routine.ColumnNames[i]);
                rowBuilder.Append("</th>");
            }
            rowBuilder.Append("</tr>");

            // Flush header
            WriteToWriter(rowBuilder, writer);
            await writer.FlushAsync(cancellationToken);
            rowBuilder.Clear();

            // Data rows
            ulong rowCount = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;
                rowBuilder.Append("<tr>");
                for (int i = 0; i < columnCount; i++)
                {
                    rowBuilder.Append("<td>");
                    object value = reader.GetValue(i);
                    if (value != DBNull.Value)
                    {
                        var str = (string)value;
                        if (descriptors[i].IsBoolean)
                        {
                            rowBuilder.Append(string.Equals(str, "t", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE");
                        }
                        else
                        {
                            HtmlEncodeAppend(rowBuilder, str.AsSpan());
                        }
                    }
                    rowBuilder.Append("</td>");
                }
                rowBuilder.Append("</tr>");

                if (bufferRows > 1 && rowCount % bufferRows == 0)
                {
                    WriteToWriter(rowBuilder, writer);
                    await writer.FlushAsync(cancellationToken);
                    rowBuilder.Clear();
                }
            }

            // Close table
            rowBuilder.Append("</table>");

            // Footer
            if (Footer is not null)
            {
                rowBuilder.Append(Footer);
            }

            if (rowBuilder.Length > 0)
            {
                WriteToWriter(rowBuilder, writer);
                await writer.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            StringBuilderPool.Return(rowBuilder);
        }
    }

    private static void HtmlEncodeAppend(StringBuilder sb, ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void WriteToWriter(StringBuilder sb, PipeWriter writer)
    {
        foreach (ReadOnlyMemory<char> chunk in sb.GetChunks())
        {
            var buffer = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chunk.Length));
            int bytesWritten = Encoding.UTF8.GetBytes(chunk.Span, buffer);
            writer.Advance(bytesWritten);
        }
    }
}
