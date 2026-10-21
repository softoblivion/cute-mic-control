using System;
using System.IO;
using System.Reflection;
namespace MicControl {
// The only place that builds runtime data paths. Portable layout: <exe folder>\cute-mic-control_data\,
// redirected by MIC_CONTROL_HOME (tests and manual runs use a scratch folder).
static class AppPaths {
    internal const string DataFolderName="cute-mic-control_data",HomeVariable="MIC_CONTROL_HOME";
    internal static string ExePath { get { Assembly entry=Assembly.GetEntryAssembly()??Assembly.GetExecutingAssembly(); return Path.GetFullPath(entry.Location); } }
    internal static string ExeDirectory { get { return Path.GetDirectoryName(ExePath); } }
    internal static bool Redirected { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(HomeVariable)); } }
    internal static string Data { get { string home=Environment.GetEnvironmentVariable(HomeVariable); return string.IsNullOrEmpty(home)?Path.Combine(ExeDirectory,DataFolderName):Path.GetFullPath(home); } }
    internal static string Settings { get { return Path.Combine(Data,"settings.xml"); } }
    internal static string Updates { get { return Path.Combine(Data,"updates"); } }
    internal static string SelfTest { get { return Path.Combine(Data,"self-test"); } }
    // Settings of 1.0.0 lived in the user profile; they are imported once when the portable file does not exist yet.
    internal static string[] ProfileSettings { get {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new string[]{ Path.Combine(local,"Mic Control","settings.xml"),Path.Combine(local,"MicMute","settings.xml") };
    } }
}
}
