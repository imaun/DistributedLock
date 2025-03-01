using NUnit.Framework;
using Medallion.Threading.SQLite;

namespace Medallion.Threading.Tests.SQLite;

[Category("CI")]
public class SQLiteConnectionOptionsBuilderTest
{

    [Test]
    public void TestValidatesArgument()
    {
        var builder = new SQLiteConnectionOptionsBuilder();
            
        // SQLite options currently only have UseWriteAheadLogging
        Assert.DoesNotThrow(() => builder.UseWriteAheadLogging(true));
        Assert.DoesNotThrow(() => builder.UseWriteAheadLogging(false));
    }
    
    [Test]
    public void TestDefaults()
    {
        var useWal = SQLiteConnectionOptionsBuilder.GetOptions(null);
        Assert.That(useWal, Is.False); // WAL mode should be off by default
            
        // Verify that an empty options builder produces the same defaults
        Assert.That(useWal, Is.EqualTo(SQLiteConnectionOptionsBuilder.GetOptions(o => { })));
    }

    [Test]
    public void TestUseWriteAheadLogging()
    {
        var useWal = SQLiteConnectionOptionsBuilder.GetOptions(o => o.UseWriteAheadLogging());
        Assert.That(useWal, Is.True);

        useWal = SQLiteConnectionOptionsBuilder.GetOptions(o => o.UseWriteAheadLogging(false));
        Assert.That(useWal, Is.False);
    }
}