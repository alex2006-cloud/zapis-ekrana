using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZapisEkrana
{
    internal sealed class MonitorInfo
    {
        public string DeviceName;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Primary;

        public string Label()
        {
            string text = "Экран " + Width + "×" + Height;
            if (Primary)
                text += " (основной)";
            return text;
        }

        public override string ToString()
        {
            return Label();
        }
    }

    internal static class Monitors
    {
        const int EnumCurrentSettings = -1;
        const int DisplayDeviceActive = 0x1;
        const int DisplayDevicePrimary = 0x4;

        public static List<MonitorInfo> All()
        {
            var list = new List<MonitorInfo>();
            for (uint i = 0; ; i++)
            {
                var device = new DisplayDevice();
                device.cb = Marshal.SizeOf(typeof(DisplayDevice));
                if (!EnumDisplayDevices(null, i, ref device, 0))
                    break;
                if ((device.StateFlags & DisplayDeviceActive) == 0)
                    continue;

                var mode = new DevMode();
                mode.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
                if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode))
                    continue;
                if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0)
                    continue;

                var info = new MonitorInfo();
                info.DeviceName = device.DeviceName;
                info.X = mode.dmPositionX;
                info.Y = mode.dmPositionY;
                info.Width = mode.dmPelsWidth;
                info.Height = mode.dmPelsHeight;
                info.Primary = (device.StateFlags & DisplayDevicePrimary) != 0;
                list.Add(info);
            }

            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                int c = a.X.CompareTo(b.X);
                if (c != 0) return c;
                return a.Y.CompareTo(b.Y);
            });
            return list;
        }

        public static MonitorInfo Pick(List<MonitorInfo> all, string deviceName, out bool fallback)
        {
            fallback = false;
            if (all == null || all.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(deviceName))
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                        return all[i];
                }
                fallback = true;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Primary)
                    return all[i];
            }
            return all[0];
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice displayDevice, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
