using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "OrderService\\orders.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"DB file not found: {dbPath}");
    return;
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
using var reader = cmd.ExecuteReader();
Console.WriteLine("Tables:");
while (reader.Read())
{
    Console.WriteLine(reader.GetString(0));
}

// If migrations history table exists, dump its contents
cmd = conn.CreateCommand();
cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
var exists = cmd.ExecuteScalar();
if (exists != null)
{
    Console.WriteLine();
    Console.WriteLine("__EFMigrationsHistory rows:");
    cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId;";
    using var r2 = cmd.ExecuteReader();
    while (r2.Read())
    {
        Console.WriteLine($"{r2.GetString(0)} | {r2.GetString(1)}");
    }
}
