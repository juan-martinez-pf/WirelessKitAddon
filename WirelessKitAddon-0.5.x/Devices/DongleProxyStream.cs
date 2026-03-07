#if OTD06

using System;
using HidSharp;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;

namespace WirelessKitAddon.Devices
{
    public class DongleProxyStream : IDeviceEndpointStream
    {
        private readonly HidStream _stream;

        public DongleProxyStream(HidStream stream)
        {
            _stream = stream;
            _stream.ReadTimeout = int.MaxValue;
        }

        public byte[] Read() => _stream.Read();

        public void Write(byte[] buffer)
        {
            // Don't forward output reports to the dongle — it can disrupt the wireless link.
            Log.Debug("DongleProxyStream", "Blocked Write: " + BitConverter.ToString(buffer));
        }

        public void GetFeature(byte[] buffer) => _stream.GetFeature(buffer);

        public void SetFeature(byte[] buffer)
        {
            // Don't forward feature reports to the dongle — sending init commands
            // (e.g. 0x02,0x02 from tablet config) disrupts the wireless connection.
            Log.Debug("DongleProxyStream", "Blocked SetFeature: " + BitConverter.ToString(buffer));
        }

        public void Dispose() => _stream.Dispose();
    }
}

#endif
