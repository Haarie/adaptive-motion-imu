using System;
using System.Text;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>CRC-8, poly 0x07, init 0 — identical to the firmware.</summary>
    public static class Crc8
    {
        public static byte Compute(byte[] d, int offset, int count, byte c = 0)
        {
            for (int i = 0; i < count; i++)
            {
                c ^= d[offset + i];
                for (int b = 0; b < 8; b++)
                    c = (c & 0x80) != 0 ? (byte)((c << 1) ^ 0x07) : (byte)(c << 1);
            }
            return c;
        }
    }

    [Flags]
    public enum ImuFlags : byte
    {
        None = 0, I2cError = 0x01, GyroCalibrated = 0x02, AccelSaturated = 0x04,
        GyroSaturated = 0x08, WhoAmIUnexpected = 0x10, Dropped = 0x20, AccelGated = 0x40,
        /// <summary>Firmware stillness detector: rig still for at least 2 s (gyro bias is being learned).</summary>
        Still = 0x80
    }

    /// <summary>One decoded IMU window, sensor frame, SI-ish units.</summary>
    public struct ImuFrame
    {
        public uint DeviceMicros;
        public double Ax, Ay, Az;        // g (specific force)
        public double Gx, Gy, Gz;        // deg/s
        public double Q0, Q1, Q2, Q3;    // attitude quaternion
        public double PeakDevG;          // max ||a|-1g| in window
        public double VibRmsG;           // high-passed accel RMS
        public ImuFlags Flags;
        public byte DroppedCount;
        public byte Seq;
        public long HostTicks;           // Stopwatch ticks at reception
    }

    /// <summary>
    /// Incremental parser for the AdaptiveMotionIMU byte stream. Not thread-safe; feed from one reader thread.
    /// </summary>
    public sealed class FrameParser
    {
        public const byte Sync0 = 0xA5, Sync1 = 0x5A, TypeImu = 0x01, TypeText = 0x02;
        public const int ImuPayloadLength = 30;

        public event Action<ImuFrame> ImuFrameReceived;
        public event Action<string> TextReceived;

        public long FramesOk { get; private set; }
        public long CrcErrors { get; private set; }
        public long SeqGaps { get; private set; }

        private readonly byte[] _buf = new byte[300];
        private int _len;
        private int _lastSeq = -1;

        public void Feed(byte[] data, int count, long hostTicks)
        {
            for (int i = 0; i < count; i++) Push(data[i], hostTicks);
        }

        private void Push(byte b, long ticks)
        {
            if (_len == 0 && b != Sync0) return;
            if (_len == 1 && b != Sync1) { _len = b == Sync0 ? 1 : 0; return; }
            _buf[_len++] = b;
            if (_len < 5) return;
            int n = _buf[3];
            if (_len < 6 + n) return;

            byte crc = Crc8.Compute(_buf, 2, 3 + n);
            if (crc != _buf[5 + n])
            {
                CrcErrors++;
                Resync(ticks);
                return;
            }
            Dispatch(_buf[2], _buf[4], n, ticks);
            _len = 0;
        }

        // On a bad CRC, rescan the buffered bytes for the next sync pair instead of dropping them all.
        private void Resync(long ticks)
        {
            for (int s = 1; s < _len - 1; s++)
            {
                if (_buf[s] == Sync0 && _buf[s + 1] == Sync1)
                {
                    int rem = _len - s;
                    var tmp = new byte[rem];
                    Array.Copy(_buf, s, tmp, 0, rem);
                    _len = 0;
                    foreach (var t in tmp) Push(t, ticks);
                    return;
                }
            }
            _len = (_len > 0 && _buf[_len - 1] == Sync0) ? 1 : 0;
            if (_len == 1) _buf[0] = Sync0;
        }

        private void Dispatch(byte type, byte seq, int n, long ticks)
        {
            FramesOk++;
            if (_lastSeq >= 0 && ((_lastSeq + 1) & 0xFF) != seq) SeqGaps++;
            _lastSeq = seq;

            if (type == TypeText)
            {
                TextReceived?.Invoke(Encoding.ASCII.GetString(_buf, 5, n));
                return;
            }
            if (type != TypeImu || n != ImuPayloadLength) return;

            int p = 5;
            var f = new ImuFrame
            {
                DeviceMicros = BitConverter.ToUInt32(_buf, p),
                Ax = I16(p + 4) / 1000.0, Ay = I16(p + 6) / 1000.0, Az = I16(p + 8) / 1000.0,
                Gx = I16(p + 10) / 10.0, Gy = I16(p + 12) / 10.0, Gz = I16(p + 14) / 10.0,
                Q0 = I16(p + 16) / 16384.0, Q1 = I16(p + 18) / 16384.0,
                Q2 = I16(p + 20) / 16384.0, Q3 = I16(p + 22) / 16384.0,
                PeakDevG = U16(p + 24) / 1000.0,
                VibRmsG = U16(p + 26) / 1000.0,
                Flags = (ImuFlags)_buf[p + 28],
                DroppedCount = _buf[p + 29],
                Seq = seq,
                HostTicks = ticks
            };
            ImuFrameReceived?.Invoke(f);
        }

        private short I16(int o) => (short)(_buf[o] | (_buf[o + 1] << 8));
        private ushort U16(int o) => (ushort)(_buf[o] | (_buf[o + 1] << 8));
    }
}
