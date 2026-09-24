using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
namespace MicControl {
static class Native {
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyData { internal uint Key, Scan, Flags, Time; internal IntPtr Extra; }
}
class KeyItem {
    internal uint Code; internal string Label;
    public override string ToString() { return Label; }
}
static class KeyCatalog {
    internal static string Name(uint code) {
        if(code>=48 && code<=57) return ((char)code).ToString();
        if(code>=65 && code<=90) return ((char)code).ToString();
        if(code>=96 && code<=105) return "Num "+(code-96);
        if(code>=112 && code<=135) return "F"+(code-111);
        switch(code) {
            case 8:return "Backspace"; case 9:return "Tab"; case 12:return "Clear"; case 13:return "Enter"; case 4109:return "Num Enter";
            case 16:return "Shift"; case 17:return "Ctrl"; case 18:return "Alt"; case 19:return "Pause"; case 20:return "Caps Lock";
            case 27:return "Esc"; case 32:return "Space"; case 33:return "Page Up"; case 34:return "Page Down";
            case 35:return "End"; case 36:return "Home"; case 37:return "←"; case 38:return "↑"; case 39:return "→"; case 40:return "↓";
            case 44:return "Print Screen"; case 45:return "Insert"; case 46:return "Delete";
            case 91:return "Left Win"; case 92:return "Right Win"; case 93:return "Menu"; case 95:return "Sleep";
            case 106:return "Num *"; case 107:return "Num +"; case 108:return "Num Separator"; case 109:return "Num −"; case 110:return "Num ."; case 111:return "Num /";
            case 144:return "Num Lock"; case 145:return "Scroll Lock";
            case 160:return "Left Shift"; case 161:return "Right Shift"; case 162:return "Left Ctrl"; case 163:return "Right Ctrl"; case 164:return "Left Alt"; case 165:return "Right Alt";
            case 166:return "Browser Back"; case 167:return "Browser Forward"; case 168:return "Browser Refresh"; case 169:return "Browser Stop"; case 170:return "Browser Search"; case 171:return "Browser Favorites"; case 172:return "Browser Home";
            case 173:return "Volume Mute"; case 174:return "Volume −"; case 175:return "Volume +"; case 176:return "Media Next"; case 177:return "Media Previous"; case 178:return "Media Stop"; case 179:return "Media Play / Pause";
            case 180:return "Launch Mail"; case 181:return "Launch Media"; case 182:return "Launch App 1"; case 183:return "Launch App 2";
            case 186:return "; / :"; case 187:return "= / +"; case 188:return ", / <"; case 189:return "− / _"; case 190:return ". / >"; case 191:return "/ / ?"; case 192:return "` / ~";
            case 219:return "[ / {"; case 220:return "\\ / |"; case 221:return "] / }"; case 222:return "Quote"; case 226:return "OEM 102";
            default: string n=((Forms.Keys)code).ToString(); uint unused; return uint.TryParse(n,out unused)?"VK 0x"+code.ToString("X2"):n;
        }
    }
    internal static List<KeyItem> All() {
        List<KeyItem> list=new List<KeyItem>();
        for(uint i=8;i<=254;i++) if(!Name(i).StartsWith("VK ")) list.Add(new KeyItem { Code=i, Label=Name(i) });
        list.Add(new KeyItem { Code=4109,Label="Num Enter" });
        for(uint i=8;i<=254;i++) if(Name(i).StartsWith("VK ")) list.Add(new KeyItem { Code=i, Label=Name(i) });
        return list;
    }
}
// Hook callbacks only update input state. UI and COM calls are queued separately.
class KeyEngine {
    internal uint Key=77, Modifiers=3; internal bool Capturing,Suspended;
    internal Action Fired; internal Action<uint,uint> Captured;
    readonly HashSet<uint> down=new HashSet<uint>(), swallowed=new HashSet<uint>();
    uint solo; bool soloUsed;
    internal static uint Modifier(uint key) { if(key==16||key==160||key==161)return 4; if(key==17||key==162||key==163)return 2; if(key==18||key==164||key==165)return 1; if(key==91||key==92)return 8; return 0; }
    internal static bool Same(uint expected,uint actual) { return expected==actual || (expected==16&&(actual==160||actual==161)) || (expected==17&&(actual==162||actual==163)) || (expected==18&&(actual==164||actual==165)); }
    uint Mods { get { uint m=0; foreach(uint k in down)m|=Modifier(k); return m; } }
    internal void BeginCapture() { Capturing=true; solo=0; soloUsed=false; }
    internal void CancelCapture() { Capturing=false; solo=0; }
    internal bool Feed(uint key,bool up) {
        uint mod=Modifier(key); uint mods=Mods;
        if(up) {
            down.Remove(key); bool eat=swallowed.Remove(key);
            if(Capturing && mod!=0 && solo==key && !soloUsed) { Capturing=false; if(Captured!=null)Captured(mods&~mod,key); solo=0; return true; }
            if(!Capturing && !Suspended && !eat && mod!=0 && solo==key && !soloUsed && Same(Key,key) && (mods&~mod)==Modifiers) { if(Fired!=null)Fired(); }
            if(solo==key)solo=0;
            return eat;
        }
        if(!down.Add(key))return Capturing || swallowed.Contains(key);
        mods=Mods;
        if(mod!=0) { solo=key; soloUsed=false; } else soloUsed=true;
        if(Capturing) {
            swallowed.Add(key);
            if(mod==0) { Capturing=false; if(Captured!=null)Captured(mods,key); }
            return true;
        }
        if(!Suspended && mod==0 && Same(Key,key) && mods==Modifiers) { swallowed.Add(key); if(Fired!=null)Fired(); return true; }
        return false;
    }
}
class KeyboardHook : IDisposable {
    IntPtr handle; Native.HookProc callback; internal readonly KeyEngine Engine=new KeyEngine();
    internal KeyboardHook() {
        callback=OnKey; handle=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
        if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    IntPtr OnKey(int code,IntPtr message,IntPtr data) {
        if(code>=0) {
            int m=message.ToInt32();
            if(m==0x100||m==0x104||m==0x101||m==0x105) {
                Native.KeyData key=(Native.KeyData)Marshal.PtrToStructure(data,typeof(Native.KeyData));
                // Ignore synthetic input; never log or persist keyboard input.
                uint codeKey=key.Key==13 && (key.Flags&1)!=0?4109u:key.Key;
                if((key.Flags&0x10)==0 && Engine.Feed(codeKey,m==0x101||m==0x105))return new IntPtr(1);
            }
        }
        return Native.CallNextHookEx(handle,code,message,data);
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { Native.UnhookWindowsHookEx(handle); handle=IntPtr.Zero; } }
}
}
