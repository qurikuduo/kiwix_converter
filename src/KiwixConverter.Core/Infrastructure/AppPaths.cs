namespace KiwixConverter.Core.Infrastructure;

public static class AppPaths
{
    public static string ApplicationDataDirectory => EnsureDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KiwixConverter"));

    public static string DatabasePath => Path.Combine(ApplicationDataDirectory, "kiwix-converter.db");

    public static string ScratchDirectory => EnsureDirectory(Path.Combine(ApplicationDataDirectory, "scratch"));

    public static string LogsDirectory => EnsureDirectory(Path.Combine(ApplicationDataDirectory, "logs"));

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}