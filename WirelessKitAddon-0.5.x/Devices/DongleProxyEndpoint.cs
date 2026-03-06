#if OTD06

using System.Collections.Generic;
using HidSharp;
using OpenTabletDriver.Plugin.Devices;

namespace WirelessKitAddon.Devices
{
    /// <summary>
    /// Wraps the wireless dongle's pen HID interface but reports
    /// the tablet's real PID so OTD matches it against stock configs.
    /// </summary>
    public class DongleProxyEndpoint : IDeviceEndpoint
    {
        private readonly HidDevice _device;
        private readonly ushort _tabletPID;

        public DongleProxyEndpoint(HidDevice donglePenDevice, ushort tabletPID)
        {
            _device = donglePenDevice;
            _tabletPID = tabletPID;
        }

        public int ProductID => _tabletPID;
        public int VendorID => _device.VendorID;
        public int InputReportLength => _device.GetMaxInputReportLength();
        public int OutputReportLength => _device.GetMaxOutputReportLength();
        public int FeatureReportLength => _device.GetMaxFeatureReportLength();

        public string Manufacturer => "Wacom Co.,Ltd.";
        public string ProductName => "Wireless Dongle Proxy";
        public string FriendlyName => "Wireless Dongle Proxy";
        public string SerialNumber => string.Empty;

        // Prefixed to avoid collisions with HidSharp's own endpoint for PID 132
        public string DevicePath => "wireless-proxy://" + _device.DevicePath;
        public bool CanOpen => _device.CanOpen;
        public IDictionary<string, string> DeviceAttributes => new Dictionary<string, string>();

        public IDeviceEndpointStream Open()
        {
            return _device.TryOpen(out var stream)
                ? new DongleProxyStream(stream)
                : null!;
        }

        public string GetDeviceString(byte index)
        {
            try { return _device.GetDeviceString(index); }
            catch { return string.Empty; }
        }
    }
}

#endif
