namespace WirelessKitAddon.Interfaces
{
    public interface IWirelessKitReport : IBatteryReport
    {
        bool IsConnected { get; set; }

        ushort TabletPID { get; set; }
    }
}