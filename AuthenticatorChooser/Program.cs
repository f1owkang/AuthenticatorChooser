using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Conventions;
using System.Windows.Forms;

namespace AuthenticatorChooser;

public static class Program {

    [STAThread]
    public static int Main(string[] args) {
        try {
            using var app = new CommandLineApplication<Startup> {
                UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw
            };
            app.Conventions.UseDefaultConventions();
            return app.Execute(args);
        } catch (CommandParsingException e) {
            Win32MessageBox.show(e.Message, $"{Startup.PROGRAM_NAME} {Startup.PROGRAM_VERSION}", Win32MessageBox.Kind.Error);
            return 1;
        }
    }

    /// <summary>Launches the WinForms application on the current (STA) thread, blocking until it exits.</summary>
    public static void launch(ChooserOptions options) {
        Startup.CURRENT_OPTIONS = options;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(options));
    }

}
