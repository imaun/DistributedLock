using Medallion.Threading.Internal;
using Medallion.Threading.Internal.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Threading.SQLite;

/// <summary>
    /// Represents a database connection specifically for SQLite, extending <see cref="DatabaseConnection"/>.
    /// </summary>
    internal sealed class SQLiteDatabaseConnection : DatabaseConnection
    {
        public SQLiteDatabaseConnection(IDbConnection connection, bool isExternallyOwned = true)
            : base(connection, isExternallyOwned: isExternallyOwned)
        {
        }

        public SQLiteDatabaseConnection(IDbTransaction transaction)
            : base(transaction, isExternallyOwned: true)
        {
        }

        public SQLiteDatabaseConnection(string connectionString)
            : this(new SqliteConnection(connectionString), isExternallyOwned: false)
        {
        }

        /// <summary>
        /// SQLite does not benefit from prepared statements in this context.
        /// </summary>
        public override bool ShouldPrepareCommands => false;

        /// <summary>
        /// Determines if an exception is caused by command cancellation.
        /// </summary>
        public override bool IsCommandCancellationException(Exception exception)
        {
            // SQLite doesn't have specific error codes for cancellation like SQL Server,
            // but we can check for generic operation cancellation exceptions.
            if (exception is OperationCanceledException || exception is TaskCanceledException)
            {
                return true;
            }

            var exceptionType = exception.GetType();
            if (exceptionType.ToString() == "Microsoft.Data.Sqlite.SqliteException")
            {
                var resultCodeProperty = exceptionType
                    .GetProperty("SqliteErrorCode", BindingFlags.Public | BindingFlags.Instance);
                Invariant.Require(resultCodeProperty != null);

                // SQLite uses SQLITE_INTERRUPT (error code 9) for cancellations.
                return resultCodeProperty != null && Equals(resultCodeProperty.GetValue(exception), 9);
            }

            return false;
        }

        /// <summary>
        /// In SQLite, the equivalent of sleeping is executing a simple `SELECT` with a delay.
        /// </summary>
        public override async Task SleepAsync(TimeSpan sleepTime, CancellationToken cancellationToken, Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor)
        {
            Invariant.Require(sleepTime >= TimeSpan.Zero && sleepTime < TimeSpan.FromDays(1));

            using var command = this.CreateCommand();
            command.SetCommandText(@"SELECT 1 FROM (SELECT sleep(@sleepTime))");
            command.AddParameter("sleepTime", sleepTime.TotalSeconds, DbType.Double);
            command.SetTimeout(sleepTime);

            await executor(command, cancellationToken).ConfigureAwait(false);
        }
    }