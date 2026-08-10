namespace UICtl.Ipc;

internal static class DaemonPaths
{
    public const string PipeName = "uictl";

    public static string BaseDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uictl");

    public static string LogPath => Path.Combine(BaseDir, "daemon.log");
}
