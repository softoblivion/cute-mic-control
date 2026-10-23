using SelfUpdate;
namespace MicControl {
// Mic Control's values for the vendored self-update library (src\vendor\selfupdate-dotnet). No WPF or WinForms here:
// the console test runner compiles this file with the library.
static class AppUpdate {
    internal const string RelaunchArguments="--startup"; // the swapped-in version starts in the tray
    internal static UpdateConfig Config() {
        return new UpdateConfig { Repo=AppInfo.Repository,AssetName=AppInfo.AssetName,AppName=AppInfo.ProductName,Version=AppInfo.Version,UpdatesDir=AppPaths.Updates,ExePath=AppPaths.ExePath,ReleaseUrlVariable=AppInfo.ReleaseUrlVariable };
    }
}
}
