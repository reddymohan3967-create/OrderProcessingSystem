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
while (reader.Read())
{
    Console.WriteLine(reader.GetString(0));
}
