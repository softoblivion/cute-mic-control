using System.Reflection;

// The single version source: build\build.ps1 and the UI read it from here. Change it only for a release.
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]
[assembly: AssemblyTitle("Mic Control")]
[assembly: AssemblyProduct("Mic Control")]
[assembly: AssemblyDescription("A tiny Windows microphone toggle with a heart glow.")]
[assembly: AssemblyCompany("softoblivion")]

namespace MicControl {
    internal static class AppInfo {
        internal const string Version = "1.1.0";
        internal const string AuthorTag = "softoblivion";
        internal const string DisplayVersion = "v1.1.0";
        internal const string ProductName = "Mic Control";
        internal const string Repository = "softoblivion/cute-mic-control";
        internal const string AssetName = "MicControl.exe";
        internal const string ReleaseUrlVariable = "MIC_CONTROL_RELEASE_URL";
    }
}
