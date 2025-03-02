using Medallion.Threading.SQLite;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System.Data;

namespace Medallion.Threading.Tests.SQLite;

[TestFixture]
public class SQLiteDistributedLockTest
{
    private const string DefaultConnectionString = "Data Source=:memory:";

        [Test]
        public void TestBadConstructorArguments()
        {
            // Null name throws
            Assert.Catch<ArgumentNullException>(() => new SQLiteDistributedLock(null!, DefaultConnectionString));
            Assert.Catch<ArgumentNullException>(() => new SQLiteDistributedLock(null!, DefaultConnectionString, exactName: true));
            // Null connection string throws
            Assert.Catch<ArgumentNullException>(() => new SQLiteDistributedLock("a", default(string)!));

            // For transactions and connections, create a dummy connection.
            using (var connection = new SqliteConnection(DefaultConnectionString))
            {
                Assert.Catch<ArgumentNullException>(() => new SQLiteDistributedLock("a", (IDbTransaction)null!));
                Assert.Catch<ArgumentNullException>(() => new SQLiteDistributedLock("a", (IDbConnection)null!));
            }
            
            // Test name length constraints:
            Assert.Catch<FormatException>(() =>
                new SQLiteDistributedLock(new string('a', SQLiteDistributedLock.MaxNameLength + 1), DefaultConnectionString, exactName: true));
            Assert.DoesNotThrow(() =>
                new SQLiteDistributedLock(new string('a', SQLiteDistributedLock.MaxNameLength), DefaultConnectionString, exactName: true));
        }

        [Test]
        public void TestGetSafeLockNameCompat()
        {
            // The safe name should be consistent with DistributedLockHelpers.ToSafeName.
            Assert.That(SQLiteDistributedLock.GetSafeName(""), Is.EqualTo(""));
            Assert.That(SQLiteDistributedLock.GetSafeName("abc"), Is.EqualTo("abc"));
            Assert.That(SQLiteDistributedLock.GetSafeName("\\"), Is.EqualTo("\\"));
            Assert.That(SQLiteDistributedLock.GetSafeName(new string('a', SQLiteDistributedLock.MaxNameLength)), Is.EqualTo(new string('a', SQLiteDistributedLock.MaxNameLength)));
            
            // For names needing escaping, we ensure the returned name does not exceed the max length.
            var safeName1 = SQLiteDistributedLock.GetSafeName(new string('\\', SQLiteDistributedLock.MaxNameLength));
            Assert.That(safeName1.Length, Is.LessThanOrEqualTo(SQLiteDistributedLock.MaxNameLength));
            var safeName2 = SQLiteDistributedLock.GetSafeName(new string('x', SQLiteDistributedLock.MaxNameLength + 1));
            Assert.That(safeName2.Length, Is.LessThanOrEqualTo(SQLiteDistributedLock.MaxNameLength));
        }

        [Test]
        public void TestSqlCommandMustParticipateInTransaction()
        {
            // In SQLite, when a transaction is active, a command that does not explicitly set the Transaction
            // property will execute in the context of the active transaction.
            using var connection = new SqliteConnection(DefaultConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var commandInTransaction = connection.CreateCommand())
            {
                commandInTransaction.Transaction = transaction;
                commandInTransaction.CommandText = "CREATE TABLE foo (id INTEGER);";
                // Execute the command; SQLite accepts multiple statements if separated properly.
                Assert.DoesNotThrowAsync(async () => await commandInTransaction.ExecuteNonQueryAsync());
            }

            using (var commandOutsideTransaction = connection.CreateCommand())
            {
                // Even without setting Transaction, the command runs under the active transaction.
                commandOutsideTransaction.CommandText = "SELECT 2;";
                var result = commandOutsideTransaction.ExecuteScalar();
            // SQLite returns numbers as Int64.
            Assert.That(result, Is.EqualTo(2L));
            }

            using (var commandInTransaction = connection.CreateCommand())
            {
                commandInTransaction.Transaction = transaction;
                commandInTransaction.CommandText = "SELECT COUNT(*) FROM foo;";
                var result = commandInTransaction.ExecuteScalar();
                Assert.That(result, Is.EqualTo(0L));
            }
        }
}