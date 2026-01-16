using System.Runtime.InteropServices;

namespace StageZero.Services;

/// <summary>
/// Provides platform-specific paths for application data storage.
/// Uses standard locations for each operating system:
/// - macOS: ~/Library/Application Support/StageZero/
/// - Windows: %APPDATA%\StageZero\
/// - Linux: ~/.config/stagezero/
/// </summary>
public static class DataPathService
{
    private const string AppName = "StageZero";
    private const string AppNameLinux = "stagezero"; // Linux convention: lowercase

    /// <summary>
    /// Checks if the application is running inside a Docker container.
    /// </summary>
    private static bool IsRunningInDocker()
    {
        // Check for Docker-specific environment variable
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return true;

        // Check for .dockerenv file (common in Docker containers)
        if (File.Exists("/.dockerenv"))
            return true;

        // Check /proc/1/cgroup for docker (Linux containers)
        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var content = File.ReadAllText("/proc/1/cgroup");
                if (content.Contains("docker") || content.Contains("containerd"))
                    return true;
            }
        }
        catch
        {
            // Ignore errors reading /proc/1/cgroup
        }

        return false;
    }

    /// <summary>
    /// Gets the platform-specific application data directory.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public static string GetAppDataDirectory()
    {
        string appDataPath;

        // If running in Docker, use the mounted volume path
        if (IsRunningInDocker())
        {
            appDataPath = "/app-data";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: ~/Library/Application Support/StageZero/
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appDataPath = Path.Combine(homeDir, "Library", "Application Support", AppName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: %APPDATA%\StageZero\
            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppName
            );
        }
        else
        {
            // Linux: ~/.config/stagezero/
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(homeDir, ".config");
            appDataPath = Path.Combine(configDir, AppNameLinux);
        }

        // Create directory if it doesn't exist
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        return appDataPath;
    }

    /// <summary>
    /// Gets the full path to the SQLite database file.
    /// </summary>
    public static string GetDatabasePath()
    {
        return Path.Combine(GetAppDataDirectory(), "stagezero.db");
    }

    /// <summary>
    /// Gets the full path to the logs directory.
    /// </summary>
    public static string GetLogsDirectory()
    {
        var logsPath = Path.Combine(GetAppDataDirectory(), "logs");
        
        if (!Directory.Exists(logsPath))
        {
            Directory.CreateDirectory(logsPath);
        }

        return logsPath;
    }

    /// <summary>
    /// Gets information about the current platform and data paths.
    /// Useful for debugging and logging.
    /// </summary>
    public static string GetPlatformInfo()
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Unknown";

        return $"Platform: {platform}, Data Directory: {GetAppDataDirectory()}";
    }
}

