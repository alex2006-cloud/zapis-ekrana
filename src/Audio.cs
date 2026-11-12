using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZapisEkrana
{
    internal sealed class MicInfo
    {
        public string Id;
        public string Name;

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class AudioDevices
    {
        public static List<MicInfo> ListCapture()
        {
            var list = new List<MicInfo>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.EnumAudioEndpoints(WasapiNative.Capture, WasapiNative.DeviceActive, out collection) < 0 || collection == null)
                    return list;

                int count;
                collection.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        if (collection.Item(i, out device) < 0 || device == null)
                            continue;
                        var info = new MicInfo();
                        info.Id = WasapiNative.ReadId(device);
                        info.Name = WasapiNative.ReadName(device);
                        if (string.IsNullOrEmpty(info.Name))
                            info.Name = "Микрофон " + (i + 1).ToString();
                        if (!string.IsNullOrEmpty(info.Id))
                            list.Add(info);
                    }
                    finally
                    {
                        if (device != null)
                            Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (collection != null)
                    Marshal.ReleaseComObject(collection);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
            return list;
        }
    }

    internal sealed class AudioMixer
    {
        public string Warning;
        public string Detail;

        WasapiCapture _system;
        WasapiCapture _mic;
        Thread _captureThread;
        Thread _writerThread;
        volatile bool _stopCapture;
        volatile bool _stopWriter;
        StreamSlot _output;
        readonly ManualResetEvent _ready = new ManualResetEvent(false);
        string _prepareError;

        public string Prepare(bool noMic, bool useDefaultMic, string micId)
        {
            _prepareError = null;
            Warning = null;
            Detail = null;
            _stopCapture = false;
            _ready.Reset();
            _captureThread = new Thread(CaptureThread);
            _captureThread.IsBackground = true;
            _captureThread.Name = "audio-capture";
            _captureThread.SetApartmentState(ApartmentState.MTA);
            string choice = noMic ? "" : (useDefaultMic ? null : micId);
            _captureThread.Start(choice);
            if (!_ready.WaitOne(8000))
                return "Не удалось начать запись.";
            return _prepareError;
        }

        public void StartOutput(System.IO.Stream stream)
        {
            if (_system != null)
                _system.Queue.Clear();
            if (_mic != null)
                _mic.Queue.Clear();
            _output = new StreamSlot(stream);
            _stopWriter = false;
            _writerThread = new Thread(WriterThread);
            _writerThread.IsBackground = true;
            _writerThread.Name = "audio-writer";
            _writerThread.Start();
        }

        public void Stop()
        {
            _stopWriter = true;
            if (_writerThread != null)
            {
                _writerThread.Join(2000);
                _writerThread = null;
            }
            _stopCapture = true;
            if (_captureThread != null)
            {
                _captureThread.Join(3000);
                _captureThread = null;
            }
        }

        void CaptureThread(object state)
        {
            string micId = (string)state;
            try
            {
                WasapiNative.CoInitializeEx(IntPtr.Zero, 0);
                _system = new WasapiCapture();
                if (!_system.OpenRenderLoopback())
                {
                    _prepareError = "Не удалось начать запись.";
                    Detail = _system.Detail;
                    _ready.Set();
                    return;
                }
                _system.Start();

                if (micId != "")
                {
                    _mic = new WasapiCapture();
                    bool opened = micId == null ? _mic.OpenDefaultCapture() : _mic.OpenCapture(micId);
                    if (!opened)
                    {
                        Warning = "Микрофон недоступен. Записываю экран и звук приложений.";
                        Detail = _mic.Detail;
                        _mic.Dispose();
                        _mic = null;
                    }
                    else
                    {
                        try
                        {
                            _mic.Start();
                        }
                        catch (Exception ex)
                        {
                            Warning = "Микрофон недоступен. Записываю экран и звук приложений.";
                            Detail = ex.Message;
                            _mic.Dispose();
                            _mic = null;
                        }
                    }
                }

                _ready.Set();
                while (!_stopCapture)
                {
                    _system.Poll();
                    if (_mic != null)
                    {
                        try { _mic.Poll(); }
                        catch { _mic = null; }
                    }
                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                if (_prepareError == null)
                    _prepareError = "Не удалось начать запись.";
                Detail = ex.GetType().Name + ": " + ex.Message;
                _ready.Set();
            }
            finally
            {
                if (_mic != null) _mic.Dispose();
                if (_system != null) _system.Dispose();
                _mic = null;
                _system = null;
                WasapiNative.CoUninitialize();
            }
        }

        void WriterThread()
        {
            var clock = Stopwatch.StartNew();
            long produced = 0;
            try
            {
                while (!_stopWriter)
                {
                    long target = clock.ElapsedMilliseconds * 48000L / 1000L;
                    int need = (int)(target - produced);
                    if (need <= 0)
                    {
                        Thread.Sleep(5);
                        continue;
                    }
                    if (need > 4800)
                        need = 4800;
                    WriteChunk(need);
                    produced += need;
                }
            }
            catch (System.IO.IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        void WriteChunk(int frames)
        {
            var sys = new float[frames * 2];
            var mic = new float[frames * 2];
            int sysCount = _system == null ? 0 : _system.Queue.Read(sys, frames);
            int micCount = _mic == null ? 0 : _mic.Queue.Read(mic, frames);
            var bytes = new byte[frames * 4];
            int p = 0;
            for (int i = 0; i < frames; i++)
            {
                float left = 0f;
                float right = 0f;
                if (i < sysCount)
                {
                    left += sys[i * 2] * 0.85f;
                    right += sys[i * 2 + 1] * 0.85f;
                }
                if (i < micCount)
                {
                    left += mic[i * 2];
                    right += mic[i * 2 + 1];
                }
                WriteSample(bytes, ref p, left);
                WriteSample(bytes, ref p, right);
            }
            _output.Write(bytes, 0, bytes.Length);
        }

        static void WriteSample(byte[] bytes, ref int p, float value)
        {
            if (value > 1f) value = 1f;
            if (value < -1f) value = -1f;
            short sample = (short)Math.Round(value * 32767.0);
            bytes[p++] = (byte)(sample & 0xff);
            bytes[p++] = (byte)((sample >> 8) & 0xff);
        }

        sealed class StreamSlot
        {
            readonly System.IO.Stream _stream;
            public StreamSlot(System.IO.Stream stream) { _stream = stream; }
            public void Write(byte[] buffer, int offset, int count)
            {
                _stream.Write(buffer, offset, count);
                _stream.Flush();
            }
        }
    }

    sealed class WasapiCapture : IDisposable
    {
        public readonly StereoQueue Queue = new StereoQueue(48000 * 8);
        public string Detail;
        IAudioClient _client;
        IAudioCaptureClient _capture;
        WaveInfo _info;
        readonly To48k _resampler = new To48k();
        byte[] _raw = new byte[0];
        float[] _decoded = new float[0];
        float[] _silence = new float[0];
        bool _started;

        public bool OpenRenderLoopback()
        {
            return Open(0, null, WasapiNative.Loopback);
        }

        public bool OpenDefaultCapture()
        {
            return Open(1, null, 0);
        }

        public bool OpenCapture(string deviceId)
        {
            return Open(1, deviceId, 0);
        }

        bool Open(int dataFlow, string deviceId, int flags)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr;
                if (string.IsNullOrEmpty(deviceId))
                    hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 0, out device);
                else
                    hr = enumerator.GetDevice(deviceId, out device);
                if (hr < 0 || device == null)
                {
                    Detail = "device " + hr.ToString("X8");
                    return false;
                }

                Guid iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                object activated;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out activated);
                if (hr < 0 || activated == null)
                {
                    Detail = "activate " + hr.ToString("X8");
                    return false;
                }
                _client = (IAudioClient)activated;

                IntPtr formatPtr;
                hr = _client.GetMixFormat(out formatPtr);
                if (hr < 0 || formatPtr == IntPtr.Zero)
                {
                    Detail = "format " + hr.ToString("X8");
                    return false;
                }
                try
                {
                    _info = WaveInfo.Read(formatPtr);
                    hr = _client.Initialize(0, flags, 0, 0, formatPtr, IntPtr.Zero);
                }
                finally
                {
                    WasapiNative.CoTaskMemFree(formatPtr);
                }
                if (hr < 0)
                {
                    Detail = "initialize " + hr.ToString("X8");
                    return false;
                }
                if (_info.SampleRate < 8000 || _info.Channels < 1 || _info.BlockAlign < 1)
                {
                    Detail = "bad format";
                    return false;
                }
                _resampler.SourceRate = _info.SampleRate;

                Guid captureId = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
                object service;
                hr = _client.GetService(ref captureId, out service);
                if (hr < 0 || service == null)
                {
                    Detail = "service " + hr.ToString("X8");
                    return false;
                }
                _capture = (IAudioCaptureClient)service;
                return true;
            }
            catch (Exception ex)
            {
                Detail = ex.Message;
                return false;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public void Start()
        {
            int hr = _client.Start();
            if (hr < 0)
                throw new InvalidOperationException("start " + hr.ToString("X8"));
            _started = true;
        }

        public void Poll()
        {
            if (_capture == null)
                return;
            for (int n = 0; n < 64; n++)
            {
                int packet;
                if (_capture.GetNextPacketSize(out packet) < 0 || packet <= 0)
                    return;

                IntPtr data;
                int frames;
                int flags;
                long devicePos;
                long qpc;
                int hr = _capture.GetBuffer(out data, out frames, out flags, out devicePos, out qpc);
                if (hr < 0)
                    return;
                try
                {
                    if (frames <= 0)
                        continue;
                    int produced;
                    float[] at48;
                    if ((flags & WasapiNative.Silent) != 0 || data == IntPtr.Zero)
                    {
                        Ensure(_silence, frames * 2, ref _silence);
                        at48 = _resampler.Process(_silence, frames, out produced);
                    }
                    else
                    {
                        int bytes = frames * _info.BlockAlign;
                        EnsureRaw(bytes);
                        Marshal.Copy(data, _raw, 0, bytes);
                        Ensure(_decoded, frames * 2, ref _decoded);
                        Decode(_raw, frames, _decoded);
                        at48 = _resampler.Process(_decoded, frames, out produced);
                    }
                    if (produced > 0)
                        Queue.Write(at48, 0, produced);
                }
                finally
                {
                    _capture.ReleaseBuffer(frames);
                }
            }
        }

        void Decode(byte[] raw, int frames, float[] dst)
        {
            for (int i = 0; i < frames; i++)
            {
                float left = 0f;
                float right = 0f;
                int channels = _info.Channels;
                if (channels == 1)
                {
                    left = right = Sample(raw, i, 0);
                }
                else if (channels == 2)
                {
                    left = Sample(raw, i, 0);
                    right = Sample(raw, i, 1);
                }
                else
                {
                    float fl = Sample(raw, i, 0);
                    float fr = Sample(raw, i, 1);
                    float fc = channels > 2 ? Sample(raw, i, 2) : 0f;
                    float bl = channels > 4 ? Sample(raw, i, 4) : 0f;
                    float br = channels > 5 ? Sample(raw, i, 5) : 0f;
                    left = fl + 0.5f * fc + 0.5f * bl;
                    right = fr + 0.5f * fc + 0.5f * br;
                }
                dst[i * 2] = left;
                dst[i * 2 + 1] = right;
            }
        }

        float Sample(byte[] raw, int frame, int channel)
        {
            int offset = frame * _info.BlockAlign + channel * _info.BytesPerSample;
            if (offset < 0 || offset + _info.BytesPerSample > raw.Length)
                return 0f;
            if (_info.IsFloat && _info.BytesPerSample >= 4)
                return BitConverter.ToSingle(raw, offset);
            if (_info.BytesPerSample == 2)
                return BitConverter.ToInt16(raw, offset) / 32768f;
            if (_info.BytesPerSample == 4)
                return BitConverter.ToInt32(raw, offset) / 2147483648f;
            if (_info.BytesPerSample == 3)
            {
                int value = raw[offset] | (raw[offset + 1] << 8) | (raw[offset + 2] << 16);
                if ((value & 0x800000) != 0)
                    value |= ~0xFFFFFF;
                return value / 8388608f;
            }
            if (_info.BytesPerSample == 1)
                return (raw[offset] - 128) / 128f;
            return 0f;
        }

        static void Ensure(float[] source, int floats, ref float[] field)
        {
            if (field.Length < floats)
                field = new float[floats];
            else
                Array.Clear(field, 0, floats);
        }

        void EnsureRaw(int bytes)
        {
            if (_raw.Length < bytes)
                _raw = new byte[bytes];
        }

        public void Dispose()
        {
            try
            {
                if (_started && _client != null)
                    _client.Stop();
            }
            catch { }
            _started = false;
            if (_capture != null)
            {
                Marshal.ReleaseComObject(_capture);
                _capture = null;
            }
            if (_client != null)
            {
                Marshal.ReleaseComObject(_client);
                _client = null;
            }
        }
    }

    sealed class StereoQueue
    {
        readonly float[] _data;
        readonly int _capacity;
        int _read;
        int _count;
        readonly object _gate = new object();

        public StereoQueue(int frames)
        {
            _capacity = frames;
            _data = new float[frames * 2];
        }

        public void Clear()
        {
            lock (_gate)
            {
                _read = 0;
                _count = 0;
            }
        }

        public void Write(float[] interleaved, int offsetFrames, int frames)
        {
            lock (_gate)
            {
                for (int i = 0; i < frames; i++)
                {
                    int write = (_read + _count) % _capacity;
                    if (_count == _capacity)
                    {
                        _read = (_read + 1) % _capacity;
                        _count--;
                    }
                    _data[write * 2] = interleaved[(offsetFrames + i) * 2];
                    _data[write * 2 + 1] = interleaved[(offsetFrames + i) * 2 + 1];
                    _count++;
                }
            }
        }

        public int Read(float[] destination, int frames)
        {
            lock (_gate)
            {
                int n = frames;
                if (n > _count)
                    n = _count;
                for (int i = 0; i < n; i++)
                {
                    destination[i * 2] = _data[_read * 2];
                    destination[i * 2 + 1] = _data[_read * 2 + 1];
                    _read = (_read + 1) % _capacity;
                }
                _count -= n;
                return n;
            }
        }
    }

    sealed class To48k
    {
        public int SourceRate = 48000;
        double _nextPos;
        long _seen;
        float _lastL;
        float _lastR;
        bool _hasLast;

        public float[] Process(float[] source, int frames, out int produced)
        {
            var acc = new List<float>(frames * 2 + 8);
            if (SourceRate < 1000)
                SourceRate = 48000;
            double step = SourceRate / 48000.0;
            long packetStart = _seen;
            while (_nextPos < packetStart + frames - 0.0001)
            {
                long i0 = (long)Math.Floor(_nextPos);
                float frac = (float)(_nextPos - i0);
                float l0, r0, l1, r1;
                if (!Get(source, packetStart, frames, i0, out l0, out r0))
                    break;
                if (!Get(source, packetStart, frames, i0 + 1, out l1, out r1))
                    break;
                acc.Add(l0 + (l1 - l0) * frac);
                acc.Add(r0 + (r1 - r0) * frac);
                _nextPos += step;
            }
            if (frames > 0)
            {
                _lastL = source[(frames - 1) * 2];
                _lastR = source[(frames - 1) * 2 + 1];
                _hasLast = true;
            }
            _seen += frames;
            produced = acc.Count / 2;
            return acc.ToArray();
        }

        bool Get(float[] source, long packetStart, int frames, long index, out float left, out float right)
        {
            if (index == packetStart - 1 && _hasLast)
            {
                left = _lastL;
                right = _lastR;
                return true;
            }
            long offset = index - packetStart;
            if (offset >= 0 && offset < frames)
            {
                left = source[offset * 2];
                right = source[offset * 2 + 1];
                return true;
            }
            left = 0f;
            right = 0f;
            return false;
        }
    }

    struct WaveInfo
    {
        public int SampleRate;
        public int Channels;
        public int BitsPerSample;
        public int BlockAlign;
        public int BytesPerSample;
        public bool IsFloat;

        public static WaveInfo Read(IntPtr ptr)
        {
            var format = (WaveFormatEx)Marshal.PtrToStructure(ptr, typeof(WaveFormatEx));
            var info = new WaveInfo();
            info.SampleRate = (int)format.SampleRate;
            info.Channels = format.Channels;
            info.BitsPerSample = format.BitsPerSample;
            info.BlockAlign = format.BlockAlign;
            info.BytesPerSample = info.BitsPerSample / 8;
            if (info.BytesPerSample < 1)
                info.BytesPerSample = 1;
            info.IsFloat = format.FormatTag == 3;
            if (format.FormatTag == 0xFFFE)
            {
                var sub = (Guid)Marshal.PtrToStructure(new IntPtr(ptr.ToInt64() + 24), typeof(Guid));
                info.IsFloat = sub == WasapiNative.SubtypeFloat;
            }
            return info;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SampleRate;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    static class WasapiNative
    {
        public const int Capture = 1;
        public const int DeviceActive = 1;
        public const int Loopback = 0x00020000;
        public const int Silent = 2;
        public static readonly Guid SubtypeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr ptr);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant value);

        public static string ReadId(IMMDevice device)
        {
            IntPtr ptr;
            if (device.GetId(out ptr) < 0 || ptr == IntPtr.Zero)
                return "";
            string id = Marshal.PtrToStringUni(ptr);
            Marshal.FreeCoTaskMem(ptr);
            return id ?? "";
        }

        public static string ReadName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(0, out store) < 0 || store == null)
                    return "";
                string name = ReadProp(store, 14);
                if (string.IsNullOrEmpty(name))
                    name = ReadProp(store, 2);
                return name ?? "";
            }
            finally
            {
                if (store != null)
                    Marshal.ReleaseComObject(store);
            }
        }

        static string ReadProp(IPropertyStore store, uint pid)
        {
            var key = new PropertyKey();
            key.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
            key.pid = pid;
            PropVariant value = new PropVariant();
            try
            {
                if (store.GetValue(ref key, out value) < 0)
                    return "";
                if ((value.vt == 31 || value.vt == 8) && value.pointer != IntPtr.Zero)
                    return Marshal.PtrToStringUni(value.pointer);
                return "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);
        [PreserveSig]
        int Item(int index, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig]
        int GetId(out IntPtr id);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int count);
        [PreserveSig]
        int GetAt(int index, out PropertyKey key);
        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig]
        int GetBufferSize(out uint bufferFrameCount);
        [PreserveSig]
        int GetStreamLatency(out long latency);
        [PreserveSig]
        int GetCurrentPadding(out uint paddingFrames);
        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig]
        int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig]
        int Start();
        [PreserveSig]
        int Stop();
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);
        [PreserveSig]
        int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        [PreserveSig]
        int ReleaseBuffer(int frames);
        [PreserveSig]
        int GetNextPacketSize(out int frames);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;
        [FieldOffset(8)]
        public IntPtr pointer;
    }
}
