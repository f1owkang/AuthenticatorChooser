using ManagedWinapi.Windows;

namespace PasskeyPick.WindowOpening;

public interface WindowListener: IDisposable {

    event EventHandler<SystemWindow>? windowOpened;

}

public class WindowListenerImpl: WindowListener {

    public event EventHandler<SystemWindow>? windowOpened;

    private readonly ShellHook shellHook = new ShellHookImpl();

    public WindowListenerImpl() {
        shellHook.shellEvent += onWindowOpened;
    }

    private void onWindowOpened(object? sender, ShellEventArgs args) {
        if (args.shellEvent == ShellEventArgs.ShellEvent.HSHELL_WINDOWCREATED) {
            windowOpened?.Invoke(this, new SystemWindow(args.windowHandle));
        }
    }

    public void Dispose() {
        shellHook.shellEvent -= onWindowOpened;
        shellHook.Dispose();
        GC.SuppressFinalize(this);
    }

}