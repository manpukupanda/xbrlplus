using CsvHelper;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace xbrlplus;

public interface IDataFormatter
{
    string Format(DataTable table);
}

public class CsvFormatter : IDataFormatter
{
    public string Format(DataTable table)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        foreach (DataColumn column in table.Columns)
        {
            csv.WriteField(column.ColumnName);
        }
        csv.NextRecord();

        foreach (DataRow row in table.Rows)
        {
            foreach (var field in row.ItemArray)
            {
                csv.WriteField(field);
            }
            csv.NextRecord();
        }

        return writer.ToString();
    }
}

public class JsonFormatter : IDataFormatter
{
    public string Format(DataTable table)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (DataRow row in table.Rows)
        {
            var dict = new Dictionary<string, object>();
            foreach (DataColumn col in table.Columns)
            {
                dict[col.ColumnName] = row[col];
            }
            rows.Add(dict);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }
}

public class TableFormatter : IDataFormatter
{
    public string Format(DataTable table)
    {
        var sb = new StringBuilder();
        int columnCount = table.Columns.Count;

        // column headers
        sb.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));

        // separator line
        sb.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => new string('-', c.ColumnName.Length))));

        // data rows
        foreach (DataRow row in table.Rows)
        {
            var fields = row.ItemArray.Select(f => f?.ToString() ?? "(NULL)");
            sb.AppendLine(string.Join("\t", fields));
        }

        return sb.ToString();
    }
}
