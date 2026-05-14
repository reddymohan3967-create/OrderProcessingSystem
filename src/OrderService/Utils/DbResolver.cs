using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OrderService.Utils;

public static class DbResolver
{
    /// <summary>
    /// Resolve the shared SQLite connection string.
    /// If allowPrepare is true the resolver will copy a project-local DB into the shared
    /// location (backing up any existing file) or log that the DB will be created there.
    /// If allowPrepare is false it will only compute the path and warn if the DB is missing.
    /// </summary>
    public static string ResolveSqliteConnectionString(IConfiguration configuration, string? connectionString, string contentRoot, ILogger logger, bool allowPrepare)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        // If an explicit path is provided via environment variable use it first.
        var env = Environment.GetEnvironmentVariable("ORDERS_DB_PATH");
        if (!string.IsNullOrEmpty(env))
        {
            var candidate = env;
            if (!Path.IsPathRooted(candidate))
            {
                // interpret relative paths relative to contentRoot
                candidate = Path.Combine(contentRoot ?? AppContext.BaseDirectory, candidate);
            }

            if (File.Exists(candidate))
            {
                logger.LogInformation("Resolved DB path from ORDERS_DB_PATH: {Path}", candidate);
                return $"Data Source={candidate}";
            }

            var msgEnv = $"ORDERS_DB_PATH is set to '{env}' but file not found at resolved path '{candidate}'.";
            logger.LogError(msgEnv);
            throw new InvalidOperationException(msgEnv);
        }

        // Look for repository-local DB named data/orders.db starting at contentRoot
        // and walking up parent directories. This allows a single ./data/orders.db
        // at the repo root to be shared by multiple projects.
        var dir = contentRoot ?? AppContext.BaseDirectory;
        var tried = new List<string>();
        string? found = null;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "data", "orders.db");
            tried.Add(candidate);
            if (File.Exists(candidate))
            {
                found = candidate;
                break;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        if (found == null)
        {
            var msg = $"Repository-local Orders DB not found starting at {contentRoot}. Tried paths: {string.Join(';', tried)}.";
            logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("Resolved DB path (local): {Path}", found);
        return $"Data Source={found}";
    }
}
