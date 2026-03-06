using System;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using WirelessKitAddon.Lib;
using WirelessKitAddon.Lib.RPC;

#if OTD06
using HidSharp;
using System.Linq;
using OpenTabletDriver;
using OpenTabletDriver.Desktop;
using WirelessKitAddon.Devices;
#endif

namespace WirelessKitAddon
{
    [PluginName("Wireless Kit Daemon")]
    public sealed class WirelessKitDaemon : WirelessKitDaemonBase, ITool
    {
#if OTD06
        private WirelessDongleHub? _dongleHub;
#endif

        public WirelessKitDaemon()
        {
            _rpcServer ??= new RpcServer<WirelessKitDaemonBase>("WirelessKitDaemon", this);
            Instance ??= this;
        }

        #region Methods

        public override bool Initialize()
        {
#if OTD06
            // Initialize the dongle hub BEFORE base.Initialize() so it exists
            // when the handler initializes via the Ready event.
            if (OperatingSystem.IsMacOS())
                InitializeDongleHub();
#endif

            if (_rpcServer != null)
            {
                base.Initialize();
                _ = Task.Run(_rpcServer.MainAsync);
            }

            return true;
        }

#if OTD06
        private void InitializeDongleHub()
        {
            // Only one hub should exist across all daemon instances.
            // OTD re-instantiates plugins on each detection cycle, so subsequent
            // daemon instances must reuse the existing hub to avoid an infinite loop
            // (hub fires DevicesChanged → re-detect → new daemon → new hub → repeat).
            if (WirelessDongleHub.Instance != null)
            {
                _dongleHub = WirelessDongleHub.Instance;
                return;
            }

            // Check if the dongle is physically present before starting the hub
            bool donglePresent = DeviceList.Local.GetHidDevices()
                .Any(d => d.VendorID == 1386 && d.ProductID == 132);

            if (!donglePresent)
                return;

            _dongleHub = new WirelessDongleHub();
            if (!_dongleHub.Start())
            {
                _dongleHub.Dispose();
                _dongleHub = null;
                return;
            }

            // Connect the hub to OTD's device tree so proxy endpoints trigger auto-detection
            try
            {
                var driver = AppInfo.PluginManager.GetService<IDriver>() as Driver;
                if (driver != null)
                {
                    driver.CompositeDeviceHub.ConnectDeviceHub(_dongleHub);
                    Log.Write("Wireless Kit Daemon", "Wireless dongle hub connected to device tree.", LogLevel.Info);
                }
                else
                {
                    Log.Write("Wireless Kit Daemon", "Could not access driver to connect dongle hub.", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Wireless Kit Daemon", $"Failed to connect dongle hub: {ex.Message}", LogLevel.Warning);
            }
        }
#endif

        public void Dispose()
        {
#if OTD06
            // Never dispose the hub — it's a process-lifetime singleton that must
            // survive OTD's plugin re-instantiation cycles. Each detection cycle
            // disposes the daemon, but the hub must persist so it doesn't fire
            // DevicesChanged again and trigger another cycle.
            _dongleHub = null;
#endif
            _rpcServer.Dispose();
        }

        #endregion
    }
}