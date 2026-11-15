using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Jagti.Net
{
    /// <summary>Float bits without allocating (BitConverter makes garbage on older runtimes).</summary>
    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits
    {
        [FieldOffset(0)] public float f;
        [FieldOffset(0)] public uint u;
    }

    /// <summary>
    /// A growable byte buffer for building messages (little-endian). Plain C#, no Unity types, so the
    /// network layer can be tested outside Unity.
    /// </summary>
    public class NetWriter
    {
        public byte[] data;
        public int Length;

        public NetWriter(int capacity = 256)
        {
            data = new byte[capacity];
        }

        public void Clear() { Length = 0; }

        void Room(int n)
        {
            if (Length + n <= data.Length) return;
            int size = data.Length * 2;
            while (size < Length + n) size *= 2;
            Array.Resize(ref data, size);
        }

        public void Byte(byte v) { Room(1); data[Length++] = v; }
        public void Bool(bool v) { Byte(v ? (byte)1 : (byte)0); }
        public void SByte(sbyte v) { Byte(unchecked((byte)v)); }

        public void UShort(ushort v)
        {
            Room(2);
            data[Length++] = (byte)v;
            data[Length++] = (byte)(v >> 8);
        }

        public void Short(short v) { UShort(unchecked((ushort)v)); }

        public void Int(int v)
        {
            Room(4);
            uint u = unchecked((uint)v);
            data[Length++] = (byte)u;
            data[Length++] = (byte)(u >> 8);
            data[Length++] = (byte)(u >> 16);
            data[Length++] = (byte)(u >> 24);
        }

        public void Float(float v)
        {
            var b = new FloatBits { f = v };
            Int(unchecked((int)b.u));
        }

        /// <summary>A value in [min, max] packed into 16 bits.</summary>
        public void Packed(float v, float min, float max)
        {
            float t = (v - min) / (max - min);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            UShort((ushort)(t * 65535f + 0.5f));
        }

        /// <summary>An angle in degrees packed into one byte (about 1.4 degree steps).</summary>
        public void Angle(float degrees)
        {
            float a = degrees % 360f;
            if (a < 0f) a += 360f;
            Byte((byte)((int)(a / 360f * 256f + 0.5f) & 255));
        }

        public void String(string s)
        {
            if (s == null) s = "";
            byte[] b = Encoding.UTF8.GetBytes(s);
            int n = Math.Min(b.Length, 255);
            Byte((byte)n);
            Room(n);
            Buffer.BlockCopy(b, 0, data, Length, n);
            Length += n;
        }

        public void Bytes(byte[] src, int offset, int count)
        {
            Room(count);
            Buffer.BlockCopy(src, offset, data, Length, count);
            Length += count;
        }

        public byte[] ToArray()
        {
            var b = new byte[Length];
            Buffer.BlockCopy(data, 0, b, 0, Length);
            return b;
        }
    }

    /// <summary>Reads what a NetWriter wrote. Reading past the end returns zeros and sets Failed.</summary>
    public class NetReader
    {
        byte[] data;
        int pos, end, start;
        public bool Failed { get; private set; }
        public int Remaining { get { return end - pos; } }

        public NetReader() { }

        public NetReader(byte[] data, int offset, int length)
        {
            Reset(data, offset, length);
        }

        public void Reset(byte[] d, int offset, int length)
        {
            data = d;
            pos = offset;
            start = offset;
            end = offset + length;
            Failed = false;
        }

        /// <summary>Copies the whole message (from its first byte, wherever reading has got to), to pass it on.</summary>
        public void CopyWhole(NetWriter w)
        {
            w.Clear();
            w.Bytes(data, start, end - start);
        }

        bool Has(int n)
        {
            if (pos + n <= end) return true;
            Failed = true;
            pos = end;
            return false;
        }

        public byte Byte() { return Has(1) ? data[pos++] : (byte)0; }
        public bool Bool() { return Byte() != 0; }
        public sbyte SByte() { return unchecked((sbyte)Byte()); }

        public ushort UShort()
        {
            if (!Has(2)) return 0;
            ushort v = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public short Short() { return unchecked((short)UShort()); }

        public int Int()
        {
            if (!Has(4)) return 0;
            uint u = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
            pos += 4;
            return unchecked((int)u);
        }

        public float Float()
        {
            var b = new FloatBits { u = unchecked((uint)Int()) };
            return b.f;
        }

        public float Packed(float min, float max)
        {
            return min + UShort() / 65535f * (max - min);
        }

        public float Angle()
        {
            return Byte() / 256f * 360f;
        }

        public string String()
        {
            int n = Byte();
            if (!Has(n)) return "";
            string s = Encoding.UTF8.GetString(data, pos, n);
            pos += n;
            return s;
        }
    }
}
