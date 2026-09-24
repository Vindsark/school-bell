using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SchoolBell
{
    static class BellSynth
    {
        public const int Rate = 44100;

        public static float[] Electric(double seconds)
        {
            var rnd = new Random(20260901);
            const double tail = 1.8;
            int n = (int)((seconds + tail) * Rate);
            var strikes = new double[n];
            var clicks = new double[n];

            double t = 0.005;
            const double strikeRate = 24.0;
            while (t < seconds)
            {
                int i = (int)(t * Rate);
                double strength = 0.85 + 0.15 * rnd.NextDouble();
                if (t < 0.12) strength *= 0.45 + 0.55 * t / 0.12;
                strikes[i] += strength;
                for (int k = 0; k < 90 && i + k < n; k++)
                    clicks[i + k] += strength * (rnd.NextDouble() * 2 - 1) * Math.Exp(-k / 14.0);
                t += (1.0 / strikeRate) * (0.97 + 0.06 * rnd.NextDouble());
            }

            double[] freq = { 1210, 1218, 2745, 2761, 4380, 4395, 6330, 8610 };
            double[] tau = { 1.10, 1.00, 0.55, 0.50, 0.32, 0.30, 0.18, 0.10 };
            double[] amp = { 0.50, 0.40, 0.85, 0.50, 0.65, 0.35, 0.45, 0.25 };
            int modes = freq.Length;
            var a1 = new double[modes];
            var a2 = new double[modes];
            var g = new double[modes];
            for (int m = 0; m < modes; m++)
            {
                double w = 2 * Math.PI * freq[m] / Rate;
                double r = Math.Exp(-1.0 / (tau[m] * Rate));
                a1[m] = 2 * r * Math.Cos(w);
                a2[m] = r * r;
                g[m] = amp[m] * Math.Sin(w);
            }
            var y1 = new double[modes];
            var y2 = new double[modes];
            var outp = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int m = 0; m < modes; m++)
                {
                    if (strikes[i] > 0)
                    {
                        y1[m] *= 0.45;
                        y2[m] *= 0.45;
                    }
                    double y = a1[m] * y1[m] - a2[m] * y2[m] + g[m] * strikes[i];
                    y2[m] = y1[m];
                    y1[m] = y;
                    sum += y;
                }
                outp[i] = sum;
            }
            double prev = 0;
            for (int i = 0; i < n; i++)
            {
                outp[i] += 0.35 * (clicks[i] - prev);
                prev = clicks[i];
            }
            return Finish(outp, 1.4, 0.35);
        }

        public static float[] Chime()
        {
            int[] notes = { 76, 72, 74, 67, 67, 74, 76, 72 };
            double[] at = { 0.0, 0.6, 1.2, 1.8, 3.0, 3.6, 4.2, 4.8 };
            double[] ratio = { 1.0, 2.0, 3.0, 4.2, 5.4, 2.76 };
            double[] pamp = { 1.0, 0.35, 0.18, 0.10, 0.06, 0.12 };
            double[] ptau = { 1.6, 0.9, 0.55, 0.35, 0.22, 0.07 };
            int n = (int)((4.8 + 3.2) * Rate);
            var outp = new double[n];
            for (int k = 0; k < notes.Length; k++)
            {
                double f0 = 440.0 * Math.Pow(2, (notes[k] - 69) / 12.0);
                double vel = (k == 3 || k == 7) ? 1.0 : 0.85;
                int start = (int)(at[k] * Rate);
                for (int p = 0; p < ratio.Length; p++)
                {
                    double w = 2 * Math.PI * f0 * ratio[p] / Rate;
                    double r = Math.Exp(-1.0 / (ptau[p] * Rate));
                    double a1 = 2 * r * Math.Cos(w), a2 = r * r;
                    double y2 = 0, y1 = vel * pamp[p] * r * Math.Sin(w);
                    for (int i = start + 1; i < n; i++)
                    {
                        int j = i - start;
                        double atk = j < 220 ? j / 220.0 : 1.0;
                        outp[i] += y1 * atk;
                        double y = a1 * y1 - a2 * y2;
                        y2 = y1;
                        y1 = y;
                    }
                }
            }
            return Finish(outp, 1.0, 0.4);
        }

        static float[] Finish(double[] x, double drive, double fadeSeconds)
        {
            double peak = 1e-9;
            foreach (double v in x) peak = Math.Max(peak, Math.Abs(v));
            double norm = Math.Tanh(drive);
            int fade = (int)(fadeSeconds * Rate);
            var result = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double v = Math.Tanh(drive * x[i] / peak) / norm * 0.89;
                int left = x.Length - 1 - i;
                if (left < fade) v *= (double)left / fade;
                result[i] = (float)v;
            }
            return result;
        }

        public static byte[] ToWav(float[] samples, double gain)
        {
            int dataLen = samples.Length * 2;
            using (var ms = new MemoryStream(44 + dataLen))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                foreach (float f in samples)
                {
                    double v = Math.Max(-1.0, Math.Min(1.0, f * gain));
                    w.Write((short)Math.Round(v * 32767));
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static double Gain(int volumePercent)
        {
            double v = volumePercent / 100.0;
            return v * v;
        }
    }

    class BellPlayer
    {
        [DllImport("winmm.dll", SetLastError = true)]
        static extern bool PlaySound(IntPtr sound, IntPtr hmod, uint flags);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr hwndCallback);

        const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;
        const string Alias = "schoolbell";

        string cacheKey;
        byte[] cacheWav;
        double cacheSeconds;
        GCHandle pinned;
        bool mciOpen;
        DateTime playingUntil = DateTime.MinValue;

        public bool IsPlaying
        {
            get { return DateTime.Now < playingUntil; }
        }

        public void Prepare(Settings s)
        {
            double secs;
            GetBuiltin(s.Sound == SoundKind.Chime ? SoundKind.Chime : SoundKind.Electric, s.Duration, s.Volume, out secs);
        }

        byte[] GetBuiltin(string kind, int duration, int volume, out double seconds)
        {
            string key = kind + "|" + duration + "|" + volume;
            if (key != cacheKey)
            {
                float[] samples = kind == SoundKind.Chime ? BellSynth.Chime() : BellSynth.Electric(duration);
                cacheWav = BellSynth.ToWav(samples, BellSynth.Gain(volume));
                cacheSeconds = (double)samples.Length / BellSynth.Rate;
                cacheKey = key;
            }
            seconds = cacheSeconds;
            return cacheWav;
        }

        public string Play(Settings s)
        {
            Stop();
            if (s.Unmute)
            {
                try
                {
                    string note = SystemVolume.EnsureAudible();
                    if (note != null) Log.Write("Громкость Windows: " + note);
                }
                catch (Exception ex) { Log.Write("Не удалось проверить громкость Windows: " + ex.Message); }
            }
            string error = null;
            if (s.Sound == SoundKind.File)
            {
                if (PlayFile(s.SoundFile, s.Volume)) return null;
                error = "Не удалось воспроизвести файл «" + s.SoundFileTitle + "» — прозвенел стандартный звонок.";
            }
            double secs;
            byte[] wav = GetBuiltin(s.Sound == SoundKind.Chime ? SoundKind.Chime : SoundKind.Electric, s.Duration, s.Volume, out secs);
            pinned = GCHandle.Alloc(wav, GCHandleType.Pinned);
            if (!PlaySound(pinned.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
                return "Windows не смогла воспроизвести звук (нет звукового устройства?)";
            playingUntil = DateTime.Now.AddSeconds(secs);
            return error;
        }

        bool PlayFile(string path, int volume)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (Mci("open \"" + path + "\" type mpegvideo alias " + Alias) != 0) return false;
            mciOpen = true;
            Mci("setaudio " + Alias + " volume to " + (int)(1000 * BellSynth.Gain(volume)));
            if (Mci("play " + Alias) != 0)
            {
                Stop();
                return false;
            }
            double ms = 30000;
            var sb = new StringBuilder(64);
            Mci("set " + Alias + " time format milliseconds");
            if (mciSendString("status " + Alias + " length", sb, sb.Capacity, IntPtr.Zero) == 0)
            {
                double parsed;
                if (double.TryParse(sb.ToString(), out parsed) && parsed > 0) ms = parsed;
            }
            playingUntil = DateTime.Now.AddMilliseconds(ms);
            return true;
        }

        public void Stop()
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            if (pinned.IsAllocated) pinned.Free();
            if (mciOpen)
            {
                Mci("stop " + Alias);
                Mci("close " + Alias);
                mciOpen = false;
            }
            playingUntil = DateTime.MinValue;
        }

        static int Mci(string command)
        {
            return mciSendString(command, null, 0, IntPtr.Zero);
        }
    }

    static class SystemVolume
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        public static string EnsureAudible()
        {
            object enumerator = null, volumeObj = null;
            IMMDevice device = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null)
                    return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out volumeObj) != 0 || volumeObj == null)
                    return null;
                var volume = (IAudioEndpointVolume)volumeObj;
                Guid ctx = Guid.Empty;
                string note = null;
                bool muted;
                if (volume.GetMute(out muted) == 0 && muted)
                {
                    volume.SetMute(false, ref ctx);
                    note = "звук был выключен — включён";
                }
                float level;
                if (volume.GetMasterVolumeLevelScalar(out level) == 0 && level < 0.1f)
                {
                    volume.SetMasterVolumeLevelScalar(0.5f, ref ctx);
                    note = (note == null ? "" : note + "; ") + "громкость была " + (int)(level * 100) + "% — поднята до 50%";
                }
                return note;
            }
            finally
            {
                if (volumeObj != null) Marshal.ReleaseComObject(volumeObj);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    static class Power
    {
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        public static void KeepAwake(bool on)
        {
            SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
        }
    }
}
