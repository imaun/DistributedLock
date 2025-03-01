using Medallion.Threading.Internal;
using Medallion.Threading.SQLite;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Medallion.Threading.Tests.SQLite;

[Category("CI")]
public class SQLiteDatabaseConnectionTest
{
    
    [Test, Combinatorial]
    public async Task TestExecuteNonQueryAlreadyCanceled([Values] bool isAsync)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await using var connection = CreateConnection();
        await connection.OpenAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.SetCommandText("SELECT 1;");

        if (isAsync)
        {
            Assert.CatchAsync<OperationCanceledException>(() =>
                command.ExecuteNonQueryAsync(cancellationTokenSource.Token).AsTask()
            );
        }
        else
        {
            Assert.Catch<OperationCanceledException>(() =>
                SyncViaAsync.Run(_ => command.ExecuteNonQueryAsync(cancellationTokenSource.Token), 0)
            );
        }
    }

    [Test, Combinatorial]
    public async Task TestExecuteNonQueryCanCancel([Values] bool isAsync)
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        await using var connection = CreateConnection();
        await connection.OpenAsync(CancellationToken.None);
        
        // Access the underlying SqliteConnection to create a custom function for cancellation.
        var sqliteConn = connection.InnerConnection as SqliteConnection;
        sqliteConn.CreateFunction("should_cancel", () =>
        {
            if (cancellationTokenSource.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
            return 1;
        });
        
        using var command = connection.CreateCommand();
        // Create a query that uses the custom sleep function repeatedly.
        // This recursive CTE will run 1000 iterations, sleeping 0.1 sec each iteration (~100 sec total).
        command.SetCommandText(
            "WITH RECURSIVE cnt(x) AS (" +
            "   SELECT 1 " +
            "   UNION ALL " +
            "   SELECT x+1 FROM cnt LIMIT 1000" +
            ") " +
            "SELECT sum(sleep(0.1)) FROM cnt;"
        );

        var task = Task.Run(async () =>
        {
            if (isAsync)
            {
                await command.ExecuteNonQueryAsync(cancellationTokenSource.Token);
            }
            else
            {
                SyncViaAsync.Run(_ => command.ExecuteNonQueryAsync(cancellationTokenSource.Token), 0);
            }
        });

        // Wait briefly to ensure the query has started
        await Task.Delay(500);
        Assert.That(task.IsCompleted, Is.False, "Task should not complete immediately");

        // Cancel the operation
        cancellationTokenSource.Cancel();

        try
        {
            await task;
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            // Expected exception
        }

        Assert.That(task.Status, Is.EqualTo(TaskStatus.Canceled));
    }

    private static SQLiteDatabaseConnection CreateConnection() =>
        new(new SqliteConnection("DataSource=:memory:"), isExternallyOwned: false);
}