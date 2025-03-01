using System;

namespace Medallion.Threading.SQLite
{
    /// <summary>
    /// Specifies options for connecting to and locking against an SQLite database
    /// </summary>
    public sealed class SQLiteConnectionOptionsBuilder
    {
        private bool? _useWriteAheadLogging;

        internal SQLiteConnectionOptionsBuilder() { }

        /// <summary>
        /// Enables Write-Ahead Logging (WAL) mode for better concurrency.
        /// Defaults to false.
        /// </summary>
        public SQLiteConnectionOptionsBuilder UseWriteAheadLogging(bool enable = true)
        {
            this._useWriteAheadLogging = enable;
            return this;
        }

        internal static bool GetOptions(Action<SQLiteConnectionOptionsBuilder>? optionsBuilder)
        {
            SQLiteConnectionOptionsBuilder? options;
            if (optionsBuilder != null)
            {
                options = new SQLiteConnectionOptionsBuilder();
                optionsBuilder(options);
            }
            else
            {
                options = null;
            }

            return options?._useWriteAheadLogging ?? false;
        }
    }
}