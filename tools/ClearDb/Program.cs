using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "OrderService/orders.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"DB file not found: {dbPath}");
    return;
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
using var reader = cmd.ExecuteReader();
var tables = new List<string>();
while (reader.Read())
{
    tables.Add(reader.GetString(0));
}

if (tables.Count == 0)
{
    Console.WriteLine("No user tables found to drop.");
    return;
}

Console.WriteLine("About to drop tables:");
foreach (var t in tables) Console.WriteLine(t);
Console.Write("Type YES to confirm: ");
var confirm = Console.ReadLine();
if (!string.Equals(confirm, "YES", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Aborted.");
    return;
}

using var trans = conn.BeginTransaction();
foreach (var t in tables)
{
    var drop = conn.CreateCommand();
    drop.Transaction = trans;
    drop.CommandText = $"DROP TABLE IF EXISTS \"{t}\";";
    drop.ExecuteNonQuery();
}
trans.Commit();
Console.WriteLine("Dropped tables.");
