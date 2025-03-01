using System;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Threading.Internal;
using Medallion.Threading.Internal.Data;

namespace Medallion.Threading.SQLite
{
    /// <summary>
    /// Implements <see cref="IDbSynchronizationStrategy{TLockCookie}"/> for SQLite using a table-based lock.
    /// </summary>
    internal sealed class SQLiteApplicationLock : IDbSynchronizationStrategy<object>
    {
        private static readonly object LockToken = new();
        private readonly LockMode _lockMode;

        private SQLiteApplicationLock(LockMode lockMode)
        {
            _lockMode = lockMode;
        }

        public static readonly SQLiteApplicationLock ExclusiveLock = new(LockMode.Exclusive);

        bool IDbSynchronizationStrategy<object>.IsUpgradeable => false;

        public async ValueTask<object?> TryAcquireAsync(DatabaseConnection connection, string resourceName, TimeoutValue timeout, CancellationToken cancellationToken)
        {
            try
            {
                return await ExecuteAcquireCommandAsync(connection, resourceName, timeout, cancellationToken)
                    ? LockToken
                    : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ExecuteReleaseCommandAsync(connection, resourceName, isTry: true);
                throw;
            }
        }

        public ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, object lockCookie) =>
            ExecuteReleaseCommandAsync(connection, resourceName, isTry: false);

        private async Task<bool> ExecuteAcquireCommandAsync(DatabaseConnection connection, string lockName, TimeoutValue timeout, CancellationToken cancellationToken)
        {
            using var command = CreateAcquireCommand(connection, lockName, timeout);
            var result = await command.ExecuteNonQueryAsync(cancellationToken);
            return result > 0;
        }

        private static async ValueTask ExecuteReleaseCommandAsync(DatabaseConnection connection, string lockName, bool isTry)
        {
            using var command = CreateReleaseCommand(connection, lockName, isTry);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        private DatabaseCommand CreateAcquireCommand(DatabaseConnection connection, string lockName, TimeoutValue timeout)
        {
            var command = connection.CreateCommand();
            command.SetCommandText(
                "INSERT OR IGNORE INTO Locks (LockName, Expiry) VALUES (@lockName, datetime('now', '+5 minutes'))"
            );
            command.AddParameter("lockName", lockName);
            command.SetTimeout(timeout);
            return command;
        }

        private static DatabaseCommand CreateReleaseCommand(DatabaseConnection connection, string lockName, bool isTry)
        {
            var command = connection.CreateCommand();
            command.SetCommandText("DELETE FROM Locks WHERE LockName = @lockName");
            command.AddParameter("lockName", lockName);
            return command;
        }
    }

    /// <summary>
    /// Represents different types of locks in SQLite
    /// </summary>
    internal enum LockMode
    {
        Exclusive
    }
}
