using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Utils;
using Xunit;

namespace OrderService.Tests
{
    public class DbResolverTests
    {
        [Fact]
        public void NonPreparingResolverDoesNotCopyOrCreatePublishDb()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "dbresolver-test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            // Create a fake project-local DB file
            var projectDb = Path.Combine(tempDir, "orders.db");
            File.WriteAllText(projectDb, "fake-db");

            var configuration = new ConfigurationBuilder().Build();
            var logger = NullLogger.Instance;

            // Provide a DefaultConnection that points to the project-local file
            var connectionString = $"Data Source={projectDb}";

            // Act
            // Call resolver with allowPrepare = false (non-authoritative service)
            var cs = DbResolver.ResolveSqliteConnectionString(configuration, connectionString, tempDir, logger, allowPrepare: false);

            // Assert
            Assert.StartsWith("Data Source=", cs);
            var publishPath = cs.Substring("Data Source=".Length);
            // publish file must not exist after non-preparing resolver
            Assert.False(File.Exists(publishPath), "Non-preparing resolver should not create or copy the publish DB file.");

            // cleanup
            try { File.Delete(projectDb); Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public void PreparingResolverCopiesSourceAndBacksUpExistingPublishDb()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "dbresolver-int", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            // Create a fake project-local DB file (source)
            var projectDb = Path.Combine(tempDir, "source_orders.db");
            File.WriteAllText(projectDb, "source-db-content");

            // Create an existing publish DB to simulate existing shared DB
            var publishDir = Path.Combine(tempDir, "publish");
            Directory.CreateDirectory(publishDir);
            var publishDb = Path.Combine(publishDir, "Orders.db");
            File.WriteAllText(publishDb, "old-publish-db");

            // Build configuration to point SharedDb:Path to our publish path
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SharedDb:Path", publishDb) })
                .Build();

            var logger = NullLogger.Instance;
            var connectionString = $"Data Source={projectDb}";

            // Act
            var cs = DbResolver.ResolveSqliteConnectionString(configuration, connectionString, tempDir, logger, allowPrepare: true);

            // Assert
            Assert.StartsWith("Data Source=", cs);
            var resolvedPublish = cs.Substring("Data Source=".Length);
            Assert.True(File.Exists(resolvedPublish), "Publish DB should exist after preparing resolver.");

            // Backup file should exist (old publish moved to .bak)
            var backupExists = Directory.GetFiles(Path.GetDirectoryName(resolvedPublish)!, Path.GetFileName(resolvedPublish) + ".*.bak").Length > 0;
            Assert.True(backupExists, "A timestamped backup of the previous publish DB should exist.");

            // The publish DB content should match the source
            var publishContent = File.ReadAllText(resolvedPublish);
            Assert.Equal("source-db-content", publishContent);

            // cleanup
            try { File.Delete(projectDb); File.Delete(resolvedPublish); foreach(var f in Directory.GetFiles(Path.GetDirectoryName(resolvedPublish)!)) File.Delete(f); Directory.Delete(Path.GetDirectoryName(resolvedPublish)!); Directory.Delete(tempDir, true); } catch { }
        }
    }
}
