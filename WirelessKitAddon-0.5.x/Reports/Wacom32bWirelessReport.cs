using WirelessKitAddon.Interfaces;

namespace WirelessKitAddon.Reports
{
    /// <summary>
    ///   Structure of a wireless report: <br />
    ///   report[0]: Report ID <br />
    ///   report[1]: Connection status (bit 0 = connected) <br />
    ///   report[2..4]: Unknown <br />
    ///   report[5]: Battery status (bits 0-5 = level 0-31, bit 7 = charging) <br />
    ///   report[6..7]: Tablet PID (big-endian u16) <br />
    ///   report[8..31]: Unknown <br />
    /// </summary>
    public class Wacom32bWirelessReport : IWirelessKitReport
    {
        public Wacom32bWirelessReport(byte[] report)
        {
            Raw = report;

            IsConnected = (report[1] & 0x01) != 0;
            TabletPID = (ushort)((report[6] << 8) | report[7]);

            Battery = (report[5] & 0x3F) * 100 / 31f;
            IsCharging = (report[5] & 0x80) != 0;
        }

        public byte[] Raw { get; set; }

        public bool IsConnected { get; set; }

        public ushort TabletPID { get; set; }

        public float Battery { get; set; }

        public bool IsCharging { get; set; }
    }
}