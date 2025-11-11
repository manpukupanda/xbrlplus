using Manpuku.Edinet.Xbrl;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data;
using System.Text;
using xbrlplus;

if (args.Length == 0)
{
    Console.WriteLine("Usage: xbrlplus.exe <schema or instance file path>");
    Environment.Exit(1);
}

// Input path (schema or instance)
var path = args[0];
if (!path.EndsWith(".xsd") && !path.EndsWith(".xbrl"))
{
    Console.WriteLine("Error: Please provide a valid input file — either a schema (.xsd) or XBRL (.xbrl) file.");
    Environment.Exit(1);
}

// Convert to absolute URI
var entryPointUri = new Uri(Path.GetFullPath(path));

// Build host and register services
using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((_, services) =>
    {
        services.AddTransient<IXbrlParser, XbrlParser>(); // Register XBRL parser
    })
    .Build();

// Get XBRL parser
var parser = host.Services.GetRequiredService<IXbrlParser>();

// Parse XBRL document and get DTS information
var dts = await parser.ParseAsync(entryPointUri, XbrlParser.DefaultLoaderFunc);

// Create in-memory SQLite database and store data
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
using var transaction = connection.BeginTransaction();
Dao.CreateTable(connection);
Dao.SaveAll(connection, dts);
transaction.Commit();

// Start REPL loop
Console.WriteLine("SQLite REPL is ready. Type SQL statements and end with ';' to execute. Type '.exit' to quit.");

var format = "table"; // default format
string? outputPath = null; // default output path is null (console)

await RunReplLoop(connection, format, outputPath);

// Exit program
Environment.Exit(0);

static async Task RunReplLoop(SqliteConnection connection, string format, string? outputPath)
{
    var sqlBuilder = new StringBuilder();
    while (true)
    {
        Console.Write(sqlBuilder.Length == 0 ? "SQL> " : "   > ");
        var line = Console.ReadLine();
        if (line == null) break;

        var trimmed = line.Trim();
        if (trimmed is ".exit" or ".quit" or ".q")
        {
            break;
        }

        if (trimmed == ".format" || trimmed.StartsWith(".format "))
        {
            HandleFormatCommand(trimmed, ref format);
            continue;
        }

        if (trimmed == ".output" || trimmed.StartsWith(".output "))
        {
            HandleOutputCommand(trimmed, ref outputPath);
            continue;
        }

        sqlBuilder.AppendLine(line);

        if (!trimmed.EndsWith(';')) continue;

        var sql = sqlBuilder.ToString();
        sqlBuilder.Clear();

        ExecuteSql(connection, sql, format, outputPath);
    }
}

static void HandleFormatCommand(string line, ref string format)
{
    var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 2)
    {
        var newFormat = tokens[1].ToLowerInvariant();
        if (newFormat is "table" or "csv" or "json")
        {
            format = newFormat;
            Console.WriteLine($"Output format set to '{format}'.");
        }
        else
        {
            Console.WriteLine("Supported formats: table, csv, json");
        }
    }
    else
    {
        Console.WriteLine($"Current format: {format}");
        Console.WriteLine("Usage: .format [table|csv|json]");
    }
}

static void HandleOutputCommand(string line, ref string? outputPath)
{
    var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 2)
    {
        outputPath = tokens[1];
        try
        {
            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            if (ext is not ".csv" and not ".json" and not ".txt")
            {
                Console.WriteLine("Warning: Unusual file extension. Recommended: .csv, .json, .txt");
            }

            var fileName = Path.GetFileName(outputPath);
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.Any(c => invalidChars.Contains(c)))
            {
                Console.WriteLine("Error: Output filename contains invalid characters.");
                return;
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Console.WriteLine($"Error: Directory [{dir}] does not exist.");
                return;
            }

            // Optional: test write access
            using var fs = File.Open(outputPath, FileMode.Append, FileAccess.Write);
            Console.WriteLine($"Output will be written to '{outputPath}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating output path: {ex.Message}");
        }
    }
    else if (tokens.Length == 1)
    {
        outputPath = null;
        Console.WriteLine("Output redirected to console.");
    }
    else
    {
        Console.WriteLine("Usage: .output <filename> or .output to reset");
    }
}

static IDataFormatter GetFormatter(string format) =>
    format switch
    {
        "csv" => new CsvFormatter(),
        "json" => new JsonFormatter(),
        _ => new TableFormatter()
    };

static void ExecuteSql(SqliteConnection connection, string sql, string format, string? outputPath)
{
    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        if (reader.HasRows)
        {
            var table = new DataTable();
            table.Load(reader);

            var formatter = GetFormatter(format);
            var output = formatter.Format(table);
            if (outputPath != null)
            {
                File.AppendAllText(outputPath, output + Environment.NewLine);
                Console.WriteLine($"Output written to '{outputPath}'.");
            }
            else
            {
                Console.WriteLine(output);
            }
            Console.WriteLine($"{table.Rows.Count} row(s) selected.");
        }
        else
        {
            Console.WriteLine("Query executed. No rows returned.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}