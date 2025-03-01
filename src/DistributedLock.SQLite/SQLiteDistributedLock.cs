using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Threading.Internal;
using Medallion.Threading.Internal.Data;
using Microsoft.Data.Sqlite;

namespace Medallion.Threading.SQLite
{
    /// <summary>
    /// Implements a distributed lock using an SQLite database.
    /// </summary>
    public sealed partial class SQLiteDistributedLock : IInternalDistributedLock<SQLiteDistributedLockHandle>
    {
        private readonly IDbDistributedLock _internalLock;

        /// <summary>
        /// Constructs a new lock using the provided <paramref name="name"/>.
        /// The provided <paramref name="connectionString"/> will be used to connect to the SQLite database.
        /// </summary>
        public SQLiteDistributedLock(string name, string connectionString, Action<SQLiteConnectionOptionsBuilder>? options = null, bool exactName = false)
            : this(name, exactName, n => CreateInternalLock(n, connectionString, options))
        {
        }

        /// <summary>
        /// Constructs a new lock using the provided <paramref name="name"/>.
        /// The provided <paramref name="connection"/> will be used to connect to the database and will provide lock scope.
        /// </summary>
        public SQLiteDistributedLock(string name, IDbConnection connection, bool exactName = false)
            : this(name, exactName, n => CreateInternalLock(n, connection))
        {
        }
        
        /// <summary>
        /// Constructs a new lock using the provided <paramref name="name"/>.
        /// 
        /// The provided <paramref name="transaction"/> will be used to connect to the database and will provide lock scope. 
        /// It is assumed to be externally managed and will not be committed or rolled back.
        /// 
        /// Unless <paramref name="exactName"/> is specified, <paramref name="name"/> will be escaped/hashed to ensure name validity.
        /// </summary>
        public SQLiteDistributedLock(string name, IDbTransaction transaction, bool exactName = false)
            : this(name, exactName, n => CreateInternalLock(n, transaction))
        {
        }


        private SQLiteDistributedLock(string name, bool exactName, Func<string, IDbDistributedLock> internalLockFactory)
        {
            if (exactName)
            {
                if (name == null) throw new ArgumentNullException(nameof(name));
                if (name.Length > MaxNameLength) throw new FormatException($"{nameof(name)}: must be at most {MaxNameLength} characters");
                this.Name = name;
            }
            else
            {
                this.Name = GetSafeName(name);
            }

            this._internalLock = internalLockFactory(this.Name);
        }

        /// <summary>
        /// The maximum allowed length for lock names.
        /// </summary>
        internal static int MaxNameLength => 255;

        /// <summary>
        /// Implements <see cref="IDistributedLock.Name"/>
        /// </summary>
        public string Name { get; }
        
        internal static string GetSafeName(string name) =>
            DistributedLockHelpers.ToSafeName(name, MaxNameLength, s => s);

        public SQLiteDistributedLockHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
            DistributedLockHelpers.TryAcquire(this, timeout, cancellationToken);

        public SQLiteDistributedLockHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            DistributedLockHelpers.Acquire(this, timeout, cancellationToken);

        public ValueTask<SQLiteDistributedLockHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
            this.As<IInternalDistributedLock<SQLiteDistributedLockHandle>>().InternalTryAcquireAsync(timeout, cancellationToken);

        public ValueTask<SQLiteDistributedLockHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            DistributedLockHelpers.AcquireAsync(this, timeout, cancellationToken);

        IDistributedSynchronizationHandle? IDistributedLock.TryAcquire(TimeSpan timeout, CancellationToken cancellationToken) =>
            this.TryAcquire(timeout, cancellationToken);

        IDistributedSynchronizationHandle IDistributedLock.Acquire(TimeSpan? timeout, CancellationToken cancellationToken) =>
            this.Acquire(timeout, cancellationToken);

        ValueTask<IDistributedSynchronizationHandle?> IDistributedLock.TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            this.TryAcquireAsync(timeout, cancellationToken).Convert(To<IDistributedSynchronizationHandle?>.ValueTask);

        ValueTask<IDistributedSynchronizationHandle> IDistributedLock.AcquireAsync(TimeSpan? timeout, CancellationToken cancellationToken) =>
            this.AcquireAsync(timeout, cancellationToken).Convert(To<IDistributedSynchronizationHandle>.ValueTask);

        // private void EnsureLockTableExists()
        // {
        //     using var connection = new SqliteConnection(_connectionString);
        //     connection.Open();
        //
        //     using var command = connection.CreateCommand();
        //     command.CommandText = @"
        //         CREATE TABLE IF NOT EXISTS Locks (
        //             LockName TEXT PRIMARY KEY,
        //             Expiry DATETIME NOT NULL
        //         );";
        //     command.ExecuteNonQuery();
        // }

        ValueTask<SQLiteDistributedLockHandle?> IInternalDistributedLock<SQLiteDistributedLockHandle>.InternalTryAcquireAsync(TimeoutValue timeout, CancellationToken cancellationToken) =>
            this._internalLock.TryAcquireAsync(timeout, SQLiteApplicationLock.ExclusiveLock, cancellationToken, contextHandle: null)
                .Wrap(h => new SQLiteDistributedLockHandle(h));

        internal static IDbDistributedLock CreateInternalLock(string name, string connectionString, Action<SQLiteConnectionOptionsBuilder>? optionsBuilder)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            var useWal = SQLiteConnectionOptionsBuilder.GetOptions(optionsBuilder);

            return new DedicatedConnectionOrTransactionDbDistributedLock(
                name,
                () =>
                {
                    var connection = new SqliteConnection(connectionString);
                    connection.Open();
                    if (useWal)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = "PRAGMA journal_mode=WAL;";
                        command.ExecuteNonQuery();
                    }
                    return new SQLiteDatabaseConnection(connection);
                },
                useTransaction: true,
                keepaliveCadence: Timeout.InfiniteTimeSpan
            );
        }

        internal static IDbDistributedLock CreateInternalLock(string name, IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            return new DedicatedConnectionOrTransactionDbDistributedLock(
                name,
                () => new SQLiteDatabaseConnection((SqliteConnection)connection),
                useTransaction: true,
                keepaliveCadence: Timeout.InfiniteTimeSpan
            );
        }

        internal static IDbDistributedLock CreateInternalLock(string name, IDbTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            return new DedicatedConnectionOrTransactionDbDistributedLock(
                name,
                () => new SQLiteDatabaseConnection((SqliteConnection)transaction.Connection!),
                useTransaction: true,
                keepaliveCadence: Timeout.InfiniteTimeSpan
            );
        }
    }
}
