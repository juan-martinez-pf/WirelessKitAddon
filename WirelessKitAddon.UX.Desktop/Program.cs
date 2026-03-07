using System;
using Avalonia;
using DesktopNotifications;
using DesktopNotifications.Avalonia;
using Splat;

namespace WirelessKitAddon.UX.Desktop;

sealed class Program
{
    private static INotificationManager? NotificationManager;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            Console.WriteLine("No arguments provided, Tablet Name is required.");
            return;
        }

        var builder = BuildAvaloniaApp();

        // Add the notification manager to the service provider
        Locator.CurrentMutable.RegisterConstant(NotificationManager);

        builder.StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        try
        {
            builder = builder.SetupDesktopNotifications(out NotificationManager);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Desktop notifications unavailable: {ex.Message}");
            NotificationManager = null;
        }

        return builder;
    }
}
