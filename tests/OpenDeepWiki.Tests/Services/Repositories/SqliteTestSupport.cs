using System.Runtime.InteropServices;

namespace OpenDeepWiki.Tests.Services.Repositories;

internal static class SqliteTestSupport
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(
                typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly,
                (libraryName, assembly, searchPath) => libraryName == "e_sqlite3"
                    ? NativeLibrary.Load("libsqlite3.so.0", assembly, searchPath)
                    : IntPtr.Zero);
            _initialized = true;
        }
    }
}
