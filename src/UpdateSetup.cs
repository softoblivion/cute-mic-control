using SelfUpdate;
namespace MicControl {
// Mic Control's values for the vendored self-update library (src\vendor\selfupdate-dotnet). No WPF or WinForms here:
// the console test runner compiles this file with the library.
static class AppUpdate {
    internal const string RelaunchArguments="--startup"; // the swapped-in version starts in the tray
    internal static UpdateConfig Config() {
        return new UpdateConfig { Repo=AppInfo.Repository,AssetName=AppInfo.AssetName,AppName=AppInfo.ProductName,Version=AppInfo.Version,UpdatesDir=AppPaths.Updates,ExePath=AppPaths.ExePath,ReleaseUrlVariable=AppInfo.ReleaseUrlVariable };
    }
    // The presenter's toasts are tray balloons. A toast with a button names the click in the text ("Click to restart and
    // update."), and its own text is the title; a plain toast ("Updated to v2.3.0") is the text under the app name.
    internal static void Balloon(UpdateToast toast,out string title,out string text) {
        if(toast.Action==null||string.IsNullOrEmpty(toast.ActionLabel)) { title=AppInfo.ProductName; text=toast.Text; return; }
        title=toast.Text; text="Click to "+(toast.ActionLabel==UpdateText.Restart?"restart and update":toast.ActionLabel.ToLowerInvariant())+".";
    }
}
}
