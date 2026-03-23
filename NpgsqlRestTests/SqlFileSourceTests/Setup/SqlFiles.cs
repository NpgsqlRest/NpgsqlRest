using System.Reflection;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// Partial class for SQL file creation. Each test file adds a static method
/// that writes its SQL file(s). Methods are discovered via reflection.
/// </summary>
public static partial class SqlFiles
{
    internal static string Dir { get; private set; } = null!;
    internal static string SubDir { get; private set; } = null!;

    internal static void WriteAll(string dir, string subDir)
    {
        Dir = dir;
        SubDir = subDir;
        foreach (var method in typeof(SqlFiles).GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (method.GetParameters().Length == 0 &&
                method.ReturnType == typeof(void) &&
                !string.Equals(method.Name, "WriteAll", StringComparison.OrdinalIgnoreCase))
            {
                method.Invoke(null, []);
            }
        }
    }
}
