using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderService.Data;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) ?? "C:\\ProgramData";
var dbPath = System.IO.Path.Combine(programData, "OrderProcessing", "Orders.db");
var conn = $"Data Source={dbPath}";

services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

Console.WriteLine($"DB Path: {dbPath}");
Console.WriteLine("Applied migrations:");
foreach (var m in db.Database.GetAppliedMigrations()) Console.WriteLine(m);

Console.WriteLine("Pending migrations:");
foreach (var m in db.Database.GetPendingMigrations()) Console.WriteLine(m);

Console.WriteLine("Migrations:");
foreach (var m in db.Database.GetMigrations()) Console.WriteLine(m);
