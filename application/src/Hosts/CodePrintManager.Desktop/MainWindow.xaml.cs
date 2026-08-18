using System.Globalization;
using System.Windows;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Desktop;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db;

    public MainWindow(AppDbContext db)
    {
        _db = db;
        InitializeComponent();
        RestoreWindowState();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowState();
    }

    private void RestoreWindowState()
    {
        try
        {
            var w = _db.AppConfig.Find("WindowWidth");
            var h = _db.AppConfig.Find("WindowHeight");
            var l = _db.AppConfig.Find("WindowLeft");
            var t = _db.AppConfig.Find("WindowTop");
            var m = _db.AppConfig.Find("WindowMaximized");

            if (w != null && double.TryParse(w.Value, CultureInfo.InvariantCulture, out var width) && width >= 400)
                Width = width;
            if (h != null && double.TryParse(h.Value, CultureInfo.InvariantCulture, out var height) && height >= 300)
                Height = height;
            if (l != null && t != null
                && double.TryParse(l.Value, CultureInfo.InvariantCulture, out var left)
                && double.TryParse(t.Value, CultureInfo.InvariantCulture, out var top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
            if (m != null && m.Value == "1")
                WindowState = WindowState.Maximized;
        }
        catch
        {
            // First run or corrupt config — use XAML defaults
        }
    }

    private void SaveWindowState()
    {
        try
        {
            var isMaximized = WindowState == WindowState.Maximized;

            // Save normal (restored) bounds even when maximized
            var bounds = RestoreBounds;
            SetConfig("WindowWidth", bounds.Width.ToString(CultureInfo.InvariantCulture));
            SetConfig("WindowHeight", bounds.Height.ToString(CultureInfo.InvariantCulture));
            SetConfig("WindowLeft", bounds.Left.ToString(CultureInfo.InvariantCulture));
            SetConfig("WindowTop", bounds.Top.ToString(CultureInfo.InvariantCulture));
            SetConfig("WindowMaximized", isMaximized ? "1" : "0");

            _db.SaveChanges();
        }
        catch
        {
            // Best-effort — don't crash on exit
        }
    }

    private void SetConfig(string key, string value)
    {
        var config = _db.AppConfig.Find(key);
        if (config == null)
        {
            _db.AppConfig.Add(new AppConfig { Key = key, Value = value });
        }
        else
        {
            config.Value = value;
        }
    }
}
