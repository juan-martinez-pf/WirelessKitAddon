#if OTD06

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using WirelessKitAddon.Interfaces;
using WirelessKitAddon.Reports;

namespace WirelessKitAddon.Devices
{
    /// <summary>
    ///   macOS-only custom device hub that discovers the Wacom wireless dongle via HidSharp,
    ///   reads the tablet PID from the 32-byte wireless status report, and presents a
    ///   proxy endpoint with the tablet's real PID so OTD can match it against stock configs.
    /// </summary>
    public class WirelessDongleHub : IDeviceHub, IDisposable
    {
        private const int WACOM_VID = 1386;
        private const int WIRELESS_KIT_PID = 132;
        private const int WIRELESS_REPORT_LENGTH = 32;

        private HidStream? _stream;
        private Thread? _readThread;
        private DongleProxyEndpoint? _proxyEndpoint;
        private ushort _tabletPID;
        private bool _tabletConnected;
        private bool _disposed;

        private List<IDeviceEndpoint> _previousDevices = new();

        /// <summary>Singleton instance for handler access.</summary>
        public static WirelessDongleHub? Instance { get; private set; }

        /// <summary>Raised when the proxy endpoint is added or removed.</summary>
        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

        /// <summary>Forwarded wireless reports (battery, connection, PID data).</summary>
        public event EventHandler<IDeviceReport>? Report;

        /// <summary>Raised when the reading state changes (stream opened/closed).</summary>
        public event EventHandler<bool>? ReadingChanged;

        public WirelessDongleHub()
        {
            Instance ??= this;
        }

        /// <summary>
        ///   Finds the dongle's 32-byte wireless monitor interface via HidSharp and starts
        ///   reading reports on a background thread.
        /// </summary>
        /// <returns><c>true</c> if the device was found and opened successfully.</returns>
        public bool Start()
        {
            var device = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d =>
                    d.VendorID == WACOM_VID &&
                    d.ProductID == WIRELESS_KIT_PID &&
                    d.GetMaxInputReportLength() == WIRELESS_REPORT_LENGTH &&
                    d.CanOpen);

            if (device == null)
            {
                Log.Write("WirelessDongleHub",
                    "Wireless monitor interface (32-byte) not found via HidSharp.", LogLevel.Warning);
                return false;
            }

            try
            {
                _stream = device.Open();
                _stream.ReadTimeout = Timeout.Infinite;
            }
            catch (Exception ex)
            {
                Log.Write("WirelessDongleHub",
                    $"Failed to open wireless monitor stream: {ex.Message}", LogLevel.Warning);
                _stream?.Dispose();
                _stream = null;
                return false;
            }

            _readThread = new Thread(ReadLoop)
            {
                Name = "WirelessDongleHub",
                IsBackground = true
            };
            _readThread.Start();

            Log.Write("WirelessDongleHub",
                "Wireless monitor reader started, waiting for tablet connection.", LogLevel.Info);
            return true;
        }

        private void ReadLoop()
        {
            try
            {
                ReadingChanged?.Invoke(this, true);

                var buffer = new byte[WIRELESS_REPORT_LENGTH];

                while (!_disposed && _stream != null)
                {
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead < 8 || _disposed)
                        continue;

                    var reportData = new byte[bytesRead];
                    Array.Copy(buffer, reportData, bytesRead);

                    try
                    {
                        var parsed = new Wacom32bWirelessReport(reportData);
                        OnReport(parsed);
                    }
                    catch
                    {
                        // Don't let parse errors kill the read loop
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (System.IO.IOException) { }
            catch (Exception ex)
            {
                if (!_disposed)
                    Log.Write("WirelessDongleHub",
                        $"Read loop error: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                ReadingChanged?.Invoke(this, false);

                if (!_disposed && _tabletConnected)
                {
                    _tabletConnected = false;
                    RemoveProxyEndpoint();
                }
            }
        }

        private void OnReport(IDeviceReport report)
        {
            // Forward all reports to subscribers (WirelessKitHandler uses these for battery data)
            Report?.Invoke(this, report);

            if (report is IWirelessKitReport wirelessReport)
            {
                if (wirelessReport.IsConnected && !_tabletConnected)
                {
                    _tabletConnected = true;
                    _tabletPID = wirelessReport.TabletPID;

                    if (_tabletPID != 0)
                        AddProxyEndpoint();
                }
                else if (!wirelessReport.IsConnected && _tabletConnected)
                {
                    _tabletConnected = false;
                    RemoveProxyEndpoint();
                }
            }
        }

        public IEnumerable<IDeviceEndpoint> GetDevices()
        {
            if (_proxyEndpoint != null)
                return new[] { _proxyEndpoint };
            return Array.Empty<IDeviceEndpoint>();
        }

        private void AddProxyEndpoint()
        {
            // Find the dongle's pen HID interface via HidSharp (InputReportLength 10 or 11)
            var donglePenDevice = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d =>
                    d.VendorID == WACOM_VID &&
                    d.ProductID == WIRELESS_KIT_PID &&
                    (d.GetMaxInputReportLength() == 10 || d.GetMaxInputReportLength() == 11) &&
                    d.CanOpen);

            if (donglePenDevice == null)
            {
                Log.Write("WirelessDongleHub",
                    $"Tablet PID {_tabletPID} detected but dongle pen interface not found via HidSharp.",
                    LogLevel.Warning);
                return;
            }

            _proxyEndpoint = new DongleProxyEndpoint(donglePenDevice, _tabletPID);

            Log.Write("WirelessDongleHub",
                $"Tablet connected (PID {_tabletPID}), presenting proxy endpoint.",
                LogLevel.Info);

            FireDevicesChanged();
        }

        private void RemoveProxyEndpoint()
        {
            if (_proxyEndpoint == null)
                return;

            _proxyEndpoint = null;

            Log.Write("WirelessDongleHub", "Tablet disconnected, removing proxy endpoint.", LogLevel.Info);

            FireDevicesChanged();
        }

        private void FireDevicesChanged()
        {
            var current = GetDevices().ToList();
            var args = new DevicesChangedEventArgs(_previousDevices, current);
            _previousDevices = current;

            if (args.Changes.Any())
                DevicesChanged?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _stream?.Dispose();
            _stream = null;

            _readThread?.Join(2000);
            _readThread = null;

            _proxyEndpoint = null;

            if (Instance == this)
                Instance = null;
        }
    }
}

#endif
