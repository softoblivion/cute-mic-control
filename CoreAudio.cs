using System;
using System.Runtime.InteropServices;
namespace MicControl {
static class Audio {
    internal static bool Read() { return Access(false); }
    internal static bool Toggle() { return Access(true); }
    static bool Access(bool toggle) {
        object enumerator = null; IMMDevice device = null; object endpoint = null;
        try {
            enumerator = new MMDeviceEnumerator();
            Check(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(1, 0, out device));
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
            IAudioEndpointVolume volume = (IAudioEndpointVolume)endpoint;
            bool muted; Check(volume.GetMute(out muted));
            if (toggle) { Guid context = Guid.Empty; Check(volume.SetMute(!muted, ref context)); Check(volume.GetMute(out muted)); }
            return muted;
        } finally {
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }
    static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
}
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
    [PreserveSig] int GetId(out IntPtr id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
    [PreserveSig] int VolumeStepUp(ref Guid context);
    [PreserveSig] int VolumeStepDown(ref Guid context);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
    [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
}

}
