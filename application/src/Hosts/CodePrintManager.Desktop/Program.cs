using Velopack;

namespace CodePrintManager.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack hooks: handles install/uninstall/update lifecycle.
        // Must be the VERY FIRST thing before any WPF code.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
