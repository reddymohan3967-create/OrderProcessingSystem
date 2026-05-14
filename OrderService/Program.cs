using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Interfaces;
using OrderService.Services;
using System.Text.Json.Serialization;
using System.IO;
using System.Linq;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Serialize enums as strings so Swagger shows names instead of numbers
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Ensure the API uses the project-root orders.db file (absolute path) so it's consistent
// regardless of working directory when running from the IDE.
// Use structured resolver from utility so we can inject logging and control prepare behavior
string ResolveSqliteConnectionString(string? connectionString, string contentRoot, bool allowPrepare)
{
    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var logger = loggerFactory.CreateLogger("DbResolver");
    var config = builder.Configuration;
    return OrderService.Utils.DbResolver.ResolveSqliteConnectionString(config, connectionString, contentRoot, logger, allowPrepare);
}

// Only the OrderService should prepare/create the shared DB. Pass allowPrepare = true here.
var resolvedConn = ResolveSqliteConnectionString(builder.Configuration.GetConnectionString("DefaultConnection"), builder.Environment.ContentRootPath, allowPrepare: true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(resolvedConn));

// Register EF-backed order service after DbContext registration
builder.Services.AddScoped<IOrderService, OrderService.Services.OrderService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve a simple static UI for manual testing/demo at the application root
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();

app.MapControllers();

// Perform startup DB checks and optionally apply migrations. Fail fast on errors so
// the service does not run with an uninitialized DB.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // The connection string was resolved earlier into resolvedConn. Extract the path
    // portion (expects format "Data Source=path").
    string dbPath = resolvedConn ?? string.Empty;
    const string prefix = "Data Source=";
    if (dbPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        dbPath = dbPath[prefix.Length..].Trim();

    try
    {
        var dbDir = Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory;

        // Verify we can create and delete a temporary file in the DB directory to
        // ensure we have sufficient write permissions.
        var permTestFile = Path.Combine(dbDir, $".permcheck_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(permTestFile, "permcheck");
        File.Delete(permTestFile);

        logger.LogInformation("Database directory write check succeeded for '{Dir}'", dbDir);
    }
    catch (Exception ex)
    {
        // Fail fast: missing permissions or invalid path will prevent schema creation.
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var log = loggerFactory.CreateLogger("Startup.DbInit");
        log.LogCritical(ex, "Failed write permission check for the shared DB location '{Path}'. Ensure the process has access to the directory and that the path is correct.", dbPath);
        throw;
    }

    var runMigrations = config.GetValue<bool>("Migrations:RunAtStartup", true);

    if (runMigrations)
    {
        // Use an advisory file lock to ensure only one instance applies migrations at a time.
        var lockPath = dbPath + ".migratelock";
        FileStream? lockStream = null;
        var lockTimeoutSeconds = config.GetValue<int>("Migrations:LockTimeoutSeconds", 30);
        var deadline = DateTime.UtcNow.AddSeconds(lockTimeoutSeconds);

        try
        {
            // Try to acquire exclusive lock by opening the lock file without sharing.
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    // Optionally write PID/timestamp for diagnostics
                    var info = $"PID:{Environment.ProcessId};TS:{DateTime.UtcNow:O}";
                    lockStream.SetLength(0);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(info);
                    lockStream.Write(bytes, 0, bytes.Length);
                    lockStream.Flush(true);
                    break;
                }
                catch (IOException)
                {
                    // Another process holds the lock. Wait and retry.
                    Thread.Sleep(500);
                }
            }

            if (lockStream == null)
            {
                var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
                var log = loggerFactory.CreateLogger("Startup.DbMigrations");
                log.LogCritical("Failed to acquire migration lock at '{LockPath}' within {Seconds}s. Aborting startup to avoid concurrent migrations.", lockPath, lockTimeoutSeconds);
                throw new TimeoutException("Timed out acquiring migration lock.");
            }

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            logger.LogInformation("Acquired migration lock at '{LockPath}'. Applying database migrations at startup...", lockPath);

            // Diagnostic logging: list migrations the runtime can see so we can detect
            // cases where migration .cs files are present on disk but not compiled into
            // the running assembly (which results in an empty DB with only EF tables).
            try
            {
                var known = db.Database.GetMigrations().ToList();
                var applied = db.Database.GetAppliedMigrations().ToList();
                var pending = db.Database.GetPendingMigrations().ToList();

                logger.LogInformation("EF Migrations - Known ({Count}): {Migs}", known.Count, string.Join(", ", known));
                logger.LogInformation("EF Migrations - Applied ({Count}): {Migs}", applied.Count, string.Join(", ", applied));
                logger.LogInformation("EF Migrations - Pending ({Count}): {Migs}", pending.Count, string.Join(", ", pending));

                // Development fallback: if no migrations are known (empty migrations assembly)
                // then fall back to EnsureCreated so developers can run without blocking on
                // CI-produced migrations. This only runs in Development environment and only
                // when known.Count == 0 to avoid masking missing migrations in production.
                if (builder.Environment.IsDevelopment() && known.Count == 0)
                {
                    logger.LogWarning("No EF migrations found in the running assembly. Falling back to EnsureCreated() for development only.");
                    db.Database.EnsureCreated();
                    // Skip calling Migrate below since there are no migrations to apply.
                    logger.LogInformation("Database ensured (development fallback). Skipping Migrate().");
                    // Release lock and skip migration step
                    lockStream?.Dispose();
                    if (File.Exists(lockPath)) File.Delete(lockPath);
                    goto SkipMigrate;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate EF migrations before migrating. This may indicate the migrations assembly is not available at runtime.");
            }

            // If configured to recreate the database, delete it first so migrations
            // are applied from a clean slate. Useful in development when schema has
            // significantly changed and you want to recreate tables.
            var recreate = config.GetValue<bool>("Migrations:RecreateDatabase", false);
            if (recreate)
            {
                logger.LogWarning("Migrations:RecreateDatabase=true - deleting database before applying migrations.");
                try
                {
                    db.Database.EnsureDeleted();
                    logger.LogInformation("Database deleted successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete database during recreate step. Proceeding to migrate anyway.");
                }
            }

            db.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var log = loggerFactory.CreateLogger("Startup.DbMigrations");
            log.LogCritical(ex, "An error occurred while applying database migrations. Failing fast to avoid running with an uninitialized database.");
            throw;
        }
        finally
        {
            try
            {
                lockStream?.Dispose();
                // Best-effort remove the lock file so it doesn't accumulate. Ignore failures.
                if (File.Exists(lockPath))
                {
                    File.Delete(lockPath);
                }
            }
            catch { }
        }
    }
    else
    {
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var log = loggerFactory.CreateLogger("Startup.DbMigrations");
        log.LogInformation("Automatic database migrations at startup are disabled (Migrations:RunAtStartup=false). Ensure migrations are applied via CI/CD or a single initializer.");
    }
}

app.Run();

SkipMigrate: ;
