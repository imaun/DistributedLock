// namespace DistributedLock.SQLite;
//
// public class SQLiteDistributedLock
// {
//     private readonly string _connectionString;
//     private readonly string _lockName;
//
//     public SQLiteDistributedLock(string lockName, string connectionString)
//     {
//         _lockName = lockName;
//         _connectionString = connectionString;
//     }
//
//     public IDistributedLockHandle? TryAcquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
//     {
//         return TryAcquireAsync(timeout, cancellationToken).GetAwaiter().GetResult();
//     }
//
//     public async Task<IDistributedLockHandle?> TryAcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
//     {
//         using var connection = new SqliteConnection(_connectionString);
//         await connection.OpenAsync(cancellationToken);
//         var transaction = await connection.BeginTransactionAsync(cancellationToken);
//
//         try
//         {
//             var command = connection.CreateCommand();
//             command.Transaction = transaction;
//             command.CommandText = "INSERT INTO Locks (LockName, Expiry) VALUES (@lockName, datetime('now', '+5 minutes'))";
//             command.Parameters.AddWithValue("@lockName", _lockName);
//
//             await command.ExecuteNonQueryAsync(cancellationToken);
//             return new SQLiteDistributedLockHandle(transaction);
//         }
//         catch (SqliteException)
//         {
//             await transaction.RollbackAsync();
//             return null;
//         }
//     }
// }
// }