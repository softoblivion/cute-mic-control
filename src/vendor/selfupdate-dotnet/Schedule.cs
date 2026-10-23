using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace SelfUpdate {
static class UpdateSchedule {
    // Wait before the next periodic check: 5 minutes normally; after failed checks it doubles (10, 20, 40) up to 60 minutes.
    internal static TimeSpan Wait(int failures) { int minutes=5; for(int i=0;i<failures&&minutes<60;i++)minutes*=2; return TimeSpan.FromMinutes(Math.Min(minutes,60)); }
    // False while Windows reports no network connection (a periodic check is then skipped, not counted as a failure).
    internal static bool HasConnection() { try { int flags; return InternetGetConnectedState(out flags,0); } catch { return true; } }
    [DllImport("wininet.dll")] static extern bool InternetGetConnectedState(out int flags,int reserved);
}
// Periodic checks while the app runs, on a thread-pool timer (independent of any window, so it keeps running while
// the app sits in the tray). Each tick waits UpdateSchedule.Wait(failures); a tick is skipped while Windows reports no
// connection or while an update is downloaded and waits for the restart. Every Error status counts as a failure; the
// next status that ends a check in another stage (UpToDate, Available, Ready) resets the count. The launch check is
// the host's own CheckInBackground call; Start() only arms the first periodic tick.
class UpdateScheduler : IDisposable {
    internal Func<int,TimeSpan> Wait=UpdateSchedule.Wait; internal Func<bool> Online=UpdateSchedule.HasConnection; // replaceable for tests
    readonly Updater updater; readonly Timer timer; readonly object gate=new object(); int failures; bool running,disposed;
    internal UpdateScheduler(Updater updater) { this.updater=updater; timer=new Timer(Tick); updater.Watch(Seen); }
    internal int Failures { get { lock(gate)return failures; } }
    internal bool Running { get { lock(gate)return running; } }
    internal void Start() { lock(gate) { if(disposed)return; running=true; Arm(); } }
    internal void Stop() { lock(gate) { running=false; if(!disposed)timer.Change(Timeout.Infinite,Timeout.Infinite); } }
    public void Dispose() { lock(gate) { running=false; if(disposed)return; disposed=true; timer.Dispose(); } }
    void Arm() { if(running&&!disposed)timer.Change(Wait(failures),TimeSpan.FromMilliseconds(-1)); }
    void Tick(object state) {
        lock(gate) { if(!running)return; Arm(); }
        bool online; try { online=Online(); } catch { online=true; }
        if(online&&updater.Status.Stage!=UpdateStage.Ready)updater.CheckInBackground();
    }
    void Seen(UpdateStatus s) {
        lock(gate) {
            bool error=s.Stage==UpdateStage.Error,recovered=failures>0&&(s.Stage==UpdateStage.UpToDate||s.Stage==UpdateStage.Available||s.Stage==UpdateStage.Ready);
            if(!error&&!recovered)return;
            failures=error?failures+1:0; Arm();
        }
    }
}
}
