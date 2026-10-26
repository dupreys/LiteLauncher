using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LiteLauncher.Core.Settings;

namespace LiteLauncher.App;

public partial class App : Application
{
    private static DateTime _lastErrorDialog = DateTime.MinValue;

    public static SettingsStore Store { get; } = new();

    public static AppSettings Settings { get; private set; } = new();

    public static SoundEffects Sounds { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallErrorHandlers();

        Settings = Store.Load();
        Sounds.Enabled = Settings.UiSounds;
        Sounds.Volume = Settings.UiVolume;
        Sounds.Silence(TimeSpan.FromSeconds(1)); // no blip while the first card takes focus

        // Applied before the window exists so the first frame already uses the saved theme.
        ThemeManager.Apply(Settings);

        // Hovering a control focuses it, so mouse and controller share one highlight.
        var hover = new MouseEventHandler(OnHoverFocus);
        EventManager.RegisterClassHandler(typeof(Button), Mouse.MouseEnterEvent, hover);
        EventManager.RegisterClassHandler(typeof(ToggleButton), Mouse.MouseEnterEvent, hover);
        EventManager.RegisterClassHandler(typeof(Slider), Mouse.MouseEnterEvent, hover);

        base.OnStartup(e);
    }

    private static void OnHoverFocus(object sender, MouseEventArgs e)
    {
        if (sender is UIElement { IsEnabled: true, Focusable: true, IsVisible: true } element &&
            Current.MainWindow is { IsActive: true })
        {
            element.Focus();
        }
    }

    /// <summary>
    /// An unexpected error in one button shouldn't take the whole launcher down. Errors are written to
    /// crash.log (so they can be reported) and the app keeps running.
    /// </summary>
    private void InstallErrorHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("UI thread", args.Exception);
            args.Handled = true;

            // Throttled so a repeating error can't bury the screen in dialogs.
            if ((DateTime.UtcNow - _lastErrorDialog).TotalSeconds < 15) return;
            _lastErrorDialog = DateTime.UtcNow;

            MessageBox.Show(
                $"Something went wrong, but LiteLauncher kept running.\n\n{args.Exception.GetType().Name}: {args.Exception.Message}\n\nDetails were saved to:\n{CrashLog.FilePath}",
                "LiteLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("Fatal", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("Background task", args.Exception);
            args.SetObserved();
        };
    }
}

internal static class CrashLog
{
    public static string FilePath => Path.Combine(SettingsStore.DefaultDirectory, "crash.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write: nothing more we can do.
        }
    }
}
