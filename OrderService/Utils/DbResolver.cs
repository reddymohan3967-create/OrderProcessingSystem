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
        // Always use ProgramData/OrderProcessing/Orders.db as the shared DB location
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) ?? "C:\\ProgramData";
        var publishDir = Path.Combine(programData, "OrderProcessing");
        var publishDb = Path.Combine(publishDir, "Orders.db");

        if (allowPrepare)
        {
            // Ensure directory exists
            try
            {
                Directory.CreateDirectory(publishDir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to ensure publish directory {Dir}", publishDir);
            }

            // Ensure the DB file exists. Create an empty file if missing so migrations or EF can initialize it.
            try
            {
                if (!File.Exists(publishDb))
                {
                    using (var fs = File.Create(publishDb)) { }
                    logger.LogInformation("Created shared Orders DB at {Path}", publishDb);
                }
                else
                {
                    logger.LogDebug("Shared Orders DB exists at {Path}", publishDb);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create or access shared Orders DB at {Path}", publishDb);
                throw new InvalidOperationException($"Failed to ensure shared Orders DB at {publishDb}", ex);
            }
        }
        else
        {
            // Do not create anything; only check existence
            try
            {
                if (!File.Exists(publishDb))
                {
                    logger.LogWarning("Shared Orders DB not found at {Path}. The OrderService must create it before this service can use it.", publishDb);
                }
                else
                {
                    logger.LogDebug("Shared Orders DB exists at {Path}", publishDb);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed while checking shared Orders DB at {Path}", publishDb);
            }
        }

        logger.LogInformation("Resolved shared DB path: {Path}", publishDb);

        return $"Data Source={publishDb}";
    }
}
