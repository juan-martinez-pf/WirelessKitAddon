#if OTD06

using HidSharp;
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
        public void Write(byte[] buffer) => _stream.Write(buffer);

        public void GetFeature(byte[] buffer) => _stream.GetFeature(buffer);
        public void SetFeature(byte[] buffer) => _stream.SetFeature(buffer);

        public void Dispose() => _stream.Dispose();
    }
}

#endif
