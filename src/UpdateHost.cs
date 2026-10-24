using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SelfUpdate;
namespace MicControl {
// Wires the self-update library into the app: the updater with the app's values (UpdateSetup.cs), the presenter on the UI
// thread, the periodic checks and the update window. The behaviour (when the notice opens, Install, Later, Try again, the
// toasts) is the library's UpdatePresenter; the views bind to its models: the title bar pill, the tray item and the
// update rows of the Settings sheet (SettingsWindow.ShowUpdate), the update window (UpdateWindow.Render).
//
// Order in the app: the settings window creates the host after its views exist, binds them, and calls Start() once the
// window is shown; Exit() disposes it.
sealed class UpdateHost : IDisposable {
    internal readonly Updater Updater; internal readonly UpdatePresenter Presenter;
    // The tray balloon of a toast (SettingsWindow); a toast with a button runs its action when the balloon is clicked.
    internal Action<UpdateToast> ShowToast;
    readonly UpdateScheduler scheduler; readonly UpdateWindow window; readonly List<Action<UpdateView>> views=new List<Action<UpdateView>>();
    bool started,disposed;

    // autoDownload: the stored "Download updates automatically" setting. beforeApply: saves the settings before the helper
    // starts. quit: the app's own Quit path, called once the update helper has started.
    internal UpdateHost(Window owner,ThemeLook look,ResourceDictionary resources,Func<bool> autoDownload,Action beforeApply,Action quit) {
        Dispatcher dispatcher=owner.Dispatcher;
        Updater=new Updater(AppUpdate.Config(),null);
        Updater.AutoDownload=autoDownload;
        // Status changes arrive on worker threads; the presenter runs them on the UI thread.
        Presenter=new UpdatePresenter(Updater,delegate(Action a) { dispatcher.BeginInvoke(a); });
        Presenter.ApplyUpdate=delegate { if(disposed)return false; beforeApply(); return Updater.Apply(); };
        Presenter.OnQuit=quit;
        Presenter.OnToast=delegate(UpdateToast t) { Toast(t); };
        Presenter.Changed=Render;
        // Every 5 minutes while the app runs, longer after failed checks, skipped offline and while an update waits for the restart.
        scheduler=new UpdateScheduler(Updater);
        window=new UpdateWindow(Presenter,look,resources,owner,OpenLink);
    }
    // A view of the presenter's models; it gets the current models at once and every change after that.
    internal void Bind(Action<UpdateView> view) { views.Add(view); view(Presenter.View); }
    // Once per launch, after the main window is shown: cleanup and the "updated to" toast, the launch check, then a check
    // every 5 minutes.
    internal void Start() {
        if(started||disposed)return; started=true;
        Updater.Startup(); Updater.CheckInBackground(); scheduler.Start();
    }
    // The version pill and the tray item: the sheet while a newer version is known (or its download failed), otherwise the
    // panel's action (check, try again).
    internal void PillClicked() {
        if(disposed)return;
        if(Presenter.PillClick()) { window.Raise(); return; }
        Presenter.Run(Presenter.View.Panel.Action.Command);
    }
    internal void SetScale(double factor) { window.SetScale(factor); }
    void Render(UpdateView view) {
        if(disposed)return;
        window.Render(view.Sheet);
        foreach(Action<UpdateView> v in views)v(view);
    }
    void Toast(UpdateToast t) { Action<UpdateToast> show=ShowToast; if(show!=null&&!disposed)show(t); }
    // Release-note links (https only) open in the default browser.
    bool OpenLink(string url) {
        try { using(Process p=Process.Start(new ProcessStartInfo(url) { UseShellExecute=true })) { } return true; }
        catch(Exception) { Toast(new UpdateToast { Level=ToastLevel.Error,Text=UpdateText.LinkFailed }); return false; }
    }
    public void Dispose() { if(disposed)return; disposed=true; scheduler.Dispose(); window.Shutdown(); }
}
}
