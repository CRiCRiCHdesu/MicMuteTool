using MicMuteTool.Core;
using MicMuteTool.Services;
using Forms = System.Windows.Forms;

namespace MicMuteTool;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { };
        Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
        Forms.Application.ThreadException += (_, _) => { };

        if (args.Any(arg => arg.Equals("--core", StringComparison.OrdinalIgnoreCase)))
        {
            CoreProcessLauncher.RegisterScheduledTaskForCurrentExe();
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            Forms.Application.Run(new CoreHost());
            return;
        }

        var app = new App();
        app.DispatcherUnhandledException += (_, e) => e.Handled = true;
        app.InitializeComponent();
        app.Run();
    }
}
