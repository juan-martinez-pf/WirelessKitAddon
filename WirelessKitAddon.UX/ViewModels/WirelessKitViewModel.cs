using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopNotifications;
using OpenTabletDriver.External.Common.RPC;
using Splat;
using StreamJsonRpc;
using WirelessKitAddon.Lib;
using WirelessKitAddon.UX.Extensions;

namespace WirelessKitAddon.UX.ViewModels;

using static AssetLoaderExtensions;

public partial class WirelessKitViewModel : ViewModelBase, IDisposable
{
    #region Fields

    private readonly ObservableCollection<WindowIcon> _icons = [];

    private readonly RpcClient<IWirelessKitDaemon> _daemonClient;

    private readonly INotificationManager? _notificationManager;

    private readonly string _tabletName = "No tablet detected";

    private IWirelessKitDaemon? _daemon;

    private DateTime _connectedAt;

    // Time before the battery level is checked when the tablet is connected, neccessary as the battery level is not updated immediately
    private TimeSpan _beforeActive = TimeSpan.FromSeconds(5);

    // We don't want to spam the user with notifications so we make sure that we didn't already send a notification
    private bool _pastEarlyWarning;
    private bool _pastLateWarning;

    // Keep track of the last state, shouldn't be needed when nuking the daemon plugin & passing the instance directly
    private bool _lastConnectedState;
    private float _lastBatteryLevel;
    private bool _lastChargingState;

    // Filtered battery level that only decreases when not charging, prevents tooltip flickering from sensor noise
    private float _displayedBatteryLevel = -1;

    #endregion

    #region Observable Properties

    [ObservableProperty]
    private WirelessKitInstance? _currentInstance;

    [ObservableProperty]
    private WindowIcon _currentIcon;

    [ObservableProperty]
    private string _currentToolTip = "No tablet detected";

    [ObservableProperty]
    private bool _isConnected;

    #endregion

    #region Constructors

    public WirelessKitViewModel(string[]? args)
    {
        if (args != null && args.Length > 0)
            _tabletName = args[0];

        // Name will become WirelessKitInstance-{tabletName} when nuking the daemon plugin, provided it isn't null or empty
        _daemonClient = new RpcClient<IWirelessKitDaemon>("WirelessKitDaemon");

        var icons = LoadBitmaps(
            "Assets/battery_unknown.ico",       // 0
            "Assets/battery_0.ico",             // 1
            "Assets/battery_10.ico",            // 2
            "Assets/battery_33.ico",            // 3
            "Assets/battery_50.ico",            // 4
            "Assets/battery_75.ico",            // 5
            "Assets/battery_100.ico",           // 6
            "Assets/battery_0_charging.ico",    // 7
            "Assets/battery_10_charging.ico",   // 8
            "Assets/battery_33_charging.ico",   // 9
            "Assets/battery_50_charging.ico",   // 10
            "Assets/battery_75_charging.ico",   // 11
            "Assets/battery_100_charging.ico"   // 12
        );

        foreach (var icon in icons)
            if (icon != null)
                _icons.Add(new WindowIcon(icon));

        _currentIcon = _icons[0];

        _notificationManager = Locator.Current.GetService<INotificationManager>();

        InitializeClient();
    }

    // Most of this won't be needed when nuking the daemon plugin
    private void InitializeClient()
    {
        _daemonClient.Connected += OnDaemonConnected;
        _daemonClient.Attached += OnDaemonAttached;
        _daemonClient.Disconnected += OnDaemonDisconnected;

        _ = Task.Run(ConnectRpcAsync);
    }

    #endregion

    #region Events

    public event EventHandler? CloseRequested;

    #endregion

    #region Methods

    private async Task ConnectRpcAsync()
    {
        if (_daemonClient.IsConnected)
            return;

        try
        {
            await _daemonClient.ConnectAsync();
        }
        catch (Exception e)
        {
            HandleException(e);
        }
    }

    public async Task FetchInfos()
    {
        CurrentInstance = null;

        if (_daemon != null && IsConnected)
        {
            try
            {
                // Won't be needed when nuking the daemon plugin
                CurrentInstance = await _daemon.GetInstance(_tabletName);

                //if (CurrentInstance != null) // will also be able to subscribe to it directly
                //    CurrentInstance.PropertyChanged += OnInstancePropertiesChanged;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                CurrentIcon = _icons[0];
                return;
            }

            StartMonitoringInstances();
        }
    }

    public void StartMonitoringInstances()
    {
        if (_daemon == null)
            return;

        if (CurrentInstance != null)
        {
            _beforeActive = TimeSpan.FromSeconds(CurrentInstance.TimeBeforeNotification);
            // Only seed the display filter when the daemon already has a real reading;
            // a 0 from the initial instance would lock the ratchet-down filter at 0.
            if (CurrentInstance.BatteryLevel > 0)
                _displayedBatteryLevel = CurrentInstance.BatteryLevel;

            var instance = CurrentInstance;
            Dispatcher.UIThread.Post(() =>
            {
                UpdateTrayIcon(
                    GetBatteryIcon(instance.BatteryLevel, instance.IsCharging),
                    BuildToolTip(instance));
            });
        }

        _daemon.InstanceUpdated += OnInstanceChanged;
    }

    // TODO: Maybe i don't even need a separate library for notifications on 0.6? looks like Log.Write provide an arg to potentially show a notification?
    public void HandleWarnings()
    {
        if (CurrentInstance == null)
            return;

        // We don't want to spam the user with notifications so we make sure that we didn't already send a notification

        if (CurrentInstance.BatteryLevel <= CurrentInstance.EarlyWarningSetting && !_pastEarlyWarning)
        {
            SendNotification(CurrentInstance.Name, $"Battery under {CurrentInstance.EarlyWarningSetting}%");
            _pastEarlyWarning = true;
        }
        else if (CurrentInstance.BatteryLevel > CurrentInstance.EarlyWarningSetting)
            _pastEarlyWarning = false;

        if (CurrentInstance.BatteryLevel <= CurrentInstance.LateWarningSetting && !_pastLateWarning)
        {
            SendNotification(CurrentInstance.Name, $"Battery under {CurrentInstance.LateWarningSetting}%");
            _pastLateWarning = true;
        }
        else if (CurrentInstance.BatteryLevel > CurrentInstance.LateWarningSetting)
            _pastLateWarning = false;
    }

    private void SendNotification(string title, string message)
    {
        if (_notificationManager == null)
            return;

        var notification = new Notification()
        {
            Title = title,
            Body = message
        };

        _ = Task.Run(() => _notificationManager.ShowNotification(notification));
    }

    private static string BuildToolTip(WirelessKitInstance instance)
    {
        if (instance.BatteryLevel < 0)
            return $"Tablet: {instance.Name}\nBattery Level: Unsupported";

        var tip = $"Tablet: {instance.Name}\nBattery Level: {instance.BatteryLevel}%";

        if (instance.IsCharging)
            tip += "\nBattery Status: Charging";

        return tip;
    }

    public WindowIcon GetBatteryIcon(float batteryLevel, bool isCharging)
    {
        // Ranges use midpoints between icon labels so each icon represents the closest match.
        // Charging icons are at offset +6 from their non-charging counterparts.
        int offset = isCharging ? 6 : 0;

        return batteryLevel switch
        {
            0 => _icons[1 + offset],
            > 0 and <= 20 => _icons[2 + offset],
            > 20 and <= 40 => _icons[3 + offset],
            > 40 and <= 62 => _icons[4 + offset],
            > 62 and <= 87 => _icons[5 + offset],
            > 87 => _icons[6 + offset],
            _ => _icons[0]
        };
    }

    private void UpdateTrayIcon(WindowIcon icon, string toolTip)
    {
        // Avalonia's TrayIcon XAML binding for Icon doesn't propagate changes
        // to the native NSStatusItem on macOS. Set the property directly
        // on the TrayIcon object to bypass the broken binding path.
        var trayIcons = TrayIcon.GetIcons(Application.Current!);
        if (trayIcons?.Count > 0)
            trayIcons[0].Icon = icon;

        CurrentIcon = icon;      // keep binding in sync for non-macOS
        CurrentToolTip = toolTip; // tooltip binding works fine
    }

    [RelayCommand]
    public void Quit()
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    #endregion

    #region Exception Handling

    private void HandleException(Exception e)
    {
        switch (e)
        {
            case RemoteRpcException re:
                Console.WriteLine($"An Error occured while attempting to connect to the RPC server: {re.Message}");
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("This error could have occured due to an different version of WheelAddon being used with this Interface.");

                IsConnected = false;
                break;
            case OperationCanceledException _:
                break;
            default:
                Console.WriteLine($"An unhanded exception occured: {e.Message}");

                // write the exception to a file
                File.WriteAllText("exception.txt", e.ToString());

                Console.WriteLine("The exception has been written to exception.txt");

                break;
        }
    }

    #endregion

    #region Event Handlers

    private void OnDaemonConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
    }

    private void OnDaemonAttached(object? sender, EventArgs e)
    {
        if (!IsConnected)
            return;

        _daemon = _daemonClient.Instance;
        _daemon.InstanceRemoved += OnInstanceRemoved;

        _ = Task.Run(FetchInfos);
    }

    private void OnDaemonDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        CurrentInstance = null;

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Currently in use
    private void OnInstanceChanged(object? sender, WirelessKitInstance instance)
    {
        if (instance != null && instance.Name == CurrentInstance?.Name)
        {
            var rawLevel = (float)Math.Round(instance.BatteryLevel, 2);
            CurrentInstance.IsCharging = instance.IsCharging;

            // Filter sensor noise: battery only decreases when not charging
            if (instance.IsCharging || _displayedBatteryLevel < 0)
                _displayedBatteryLevel = rawLevel;
            else
                _displayedBatteryLevel = Math.Min(_displayedBatteryLevel, rawLevel);

            CurrentInstance.BatteryLevel = _displayedBatteryLevel;

            // A timeout is needed to make sure that the battery level is updated when the tablet is connected
            if (_lastConnectedState == false && instance.IsConnected)
                _connectedAt = DateTime.Now;

            // will end up nuking the daemon plugin and use the instance itself instead if testing goes as intended
            if (CurrentInstance.BatteryLevel != _lastBatteryLevel || CurrentInstance.IsCharging != _lastChargingState)
            {
                var level = CurrentInstance.BatteryLevel;
                var charging = CurrentInstance.IsCharging;
                var current = CurrentInstance;
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateTrayIcon(
                        GetBatteryIcon(level, charging),
                        BuildToolTip(current));
                });
            }

            if (instance.TimeBeforeNotification >= 0 && // Only enable Notifications if timeout is above 0
                instance.IsConnected && !instance.IsCharging && // Only enable if connected and not charging
               (DateTime.Now - _connectedAt) > _beforeActive) // Only enable if timeout has passed
                HandleWarnings();

            // Keep track of the last state, shouldn't be needed when nuking the daemon plugin & passing the instance directly
            _lastConnectedState = instance.IsConnected;
            _lastBatteryLevel = CurrentInstance.BatteryLevel;
            _lastChargingState = CurrentInstance.IsCharging;
        }
    }

    private void OnInstanceRemoved(object? sender, WirelessKitInstance instance)
    {
        if (instance.Name == CurrentInstance?.Name) // No reasons to keep this tray icon alive
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Unused for now will be used when nuking the daemon plugin
    private void OnInstancePropertiesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WirelessKitInstance instance)
        {
            switch (e.PropertyName)
            {
                case nameof(WirelessKitInstance.BatteryLevel):
                case nameof(WirelessKitInstance.IsCharging):
                    CurrentIcon = GetBatteryIcon(instance.BatteryLevel, instance.IsCharging);
                    CurrentToolTip = BuildToolTip(instance);
                    break;
                    
                case nameof(WirelessKitInstance.IsConnected) when instance.IsConnected && _lastConnectedState == false:
                    _connectedAt = DateTime.Now;
                    break;
            }

            if (instance.TimeBeforeNotification >= 0 && // Only enable Notifications if timeout is above 0
                instance.IsConnected && !instance.IsCharging && // Only enable if connected and not charging
               (DateTime.Now - _connectedAt) > _beforeActive) // Only enable if timeout has passed
                    HandleWarnings();
        }
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        if (_daemon != null)
            _daemon.InstanceUpdated -= OnInstanceChanged;

        _daemonClient.Connected -= OnDaemonConnected;
        _daemonClient.Attached -= OnDaemonAttached;
        _daemonClient.Disconnected -= OnDaemonDisconnected;
        _daemonClient.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}
