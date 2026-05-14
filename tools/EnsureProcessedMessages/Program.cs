using Microsoft.Data.Sqlite;

// Simple tool to ensure ProcessedMessages table exists in the OrderService DB file specified by first arg
// Usage: dotnet run --project tools/EnsureProcessedMessages -- "C:\path\to\orders.db"

string dbPath = args.Length > 0 ? args[0] : "OrderService\\orders.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"DB file not found: {dbPath}");
    return;
}

Console.WriteLine($"Opening DB: {Path.GetFullPath(dbPath)}");
var connStr = $"Data Source={dbPath}";
using var conn = new SqliteConnection(connStr);
conn.Open();

void ListTables(string when)
{
    Console.WriteLine($"Tables ({when}):");
    using var lcmd = conn.CreateCommand();
    lcmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
    using var reader = lcmd.ExecuteReader();
    while (reader.Read())
    {
        Console.WriteLine(" - " + reader.GetString(0));
    }
}

ListTables("before");

// Create ProcessedMessages table if missing
var cmd = conn.CreateCommand();
cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""ProcessedMessages"" (
    ""Id"" TEXT NOT NULL PRIMARY KEY,
    ""ProcessedAtUtc"" TEXT NOT NULL
);";
cmd.ExecuteNonQuery();

ListTables("after");

Console.WriteLine("Ensured ProcessedMessages table exists (if it was missing).");
