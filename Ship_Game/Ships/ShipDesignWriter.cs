using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#pragma warning disable CA1060

namespace Ship_Game.Ships
{
    /// <summary>
    /// This writer is designed to be reused multiple times
    /// between serialization calls.
    ///
    /// Clear() can be called to reset the state, however memory
    /// will not be freed - it is cached
    /// </summary>
    public sealed unsafe class ShipDesignWriter : IDisposable
    {
        // NOTE: This implementation is both faster and more memory efficient
        //       than using a StringBuilder for this specific use case

        [StructLayout(LayoutKind.Sequential)]
        struct ByteBuffer
        {
            public byte* Data;
            public int Capacity;
            public int Size;
        }

        [DllImport("SDNative.dll")]
        static extern ByteBuffer* ByteBufferNew(int defaultCapacity);
        [DllImport("SDNative.dll")]
        static extern void ByteBufferDelete(ByteBuffer* b);
        [DllImport("SDNative.dll")]
        static extern void ByteBufferCopy(ByteBuffer* b, byte[] dst);

        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteI(ByteBuffer* b, int val);
        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteF(ByteBuffer* b, float val, int maxDecimals);
        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteD(ByteBuffer* b, double val, int maxDecimals);

        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteC(ByteBuffer* b, char ch);
        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteS(ByteBuffer* b,
            [MarshalAs(UnmanagedType.LPWStr)] string str, int len
        );
        [DllImport("SDNative.dll")]
        static extern void ByteBufferWriteKV(ByteBuffer* b,
            [MarshalAs(UnmanagedType.LPWStr)] string key, int keylen,
            [MarshalAs(UnmanagedType.LPWStr)] string val, int vallen
        );

        ByteBuffer* Buffer;
        StringBuilder Managed;
        public int Capacity => Buffer != null ? Buffer->Capacity : Managed.Capacity;

        public ShipDesignWriter(int initialCapacity = 4096)
        {
            try
            {
                Buffer = ByteBufferNew(initialCapacity);
            }
            catch (Exception e) when (IsNativeLoadFailure(e))
            {
                Managed = new StringBuilder(initialCapacity);
            }
        }

        ~ShipDesignWriter()
        {
            Destroy();
        }

        void Destroy()
        {
            if (Buffer != null)
                ByteBufferDelete(Buffer);
            Buffer = null;
        }

        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        public void Clear()
        {
            if (Buffer != null) Buffer->Size = 0;
            else Managed.Clear();
        }

        public override string ToString()
        {
            return Buffer != null ? Encoding.ASCII.GetString(Buffer->Data, Buffer->Size) : Managed.ToString();
        }

        // NOTE: This must ALWAYS COPY the bytes
        public byte[] GetASCIIBytes()
        {
            if (Buffer == null)
                return Encoding.ASCII.GetBytes(Managed.ToString());
            byte[] bytes = new byte[Buffer->Size];
            ByteBufferCopy(Buffer, bytes);
            return bytes;
        }

        public void FlushToFile(FileInfo file)
        {
            using var fs = new FileStream(file.FullName, FileMode.Create, FileAccess.Write);
            byte[] bytes = GetASCIIBytes();
            fs.Write(bytes, 0, bytes.Length);
        }

        // value
        public void Write(string value)
        {
            if (Buffer != null) ByteBufferWriteS(Buffer, value, value.Length);
            else Managed.Append(value);
        }

        // -1234
        public void Write(int value)
        {
            if (Buffer != null) ByteBufferWriteI(Buffer, value);
            else Managed.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        // -1234.456
        public void Write(float value)
        {
            if (Buffer != null) ByteBufferWriteF(Buffer, value, maxDecimals:3);
            else Managed.Append(FloatToString(value, maxDecimals: 3));
        }

        public void Write(float value, int maxDecimals)
        {
            if (Buffer != null) ByteBufferWriteF(Buffer, value, maxDecimals);
            else Managed.Append(FloatToString(value, maxDecimals));
        }

        // -1234.456
        public void Write(double value)
        {
            if (Buffer != null) ByteBufferWriteD(Buffer, value, maxDecimals:3);
            else Managed.Append(value.ToString("0.################", CultureInfo.InvariantCulture));
        }

        // [x][separator][y]
        public void Write(int x, char separator, int y)
        {
            Write(x);
            Write(separator);
            Write(y);
        }

        public void Write(char ch)
        {
            if (Buffer == null)
            {
                Managed.Append(ch);
                return;
            }

            // fastpath: already enough capacity
            if (Buffer->Size < Buffer->Capacity)
            {
                Buffer->Data[Buffer->Size++] = (byte)ch;
            }
            else
            {
                ByteBufferWriteC(Buffer, ch);
            }
        }

        // key=value\n
        public void Write<T>(string key, T value)
        {
            string val = value.ToString();
            WriteKeyValue(key, val);
        }

        // key=true|false\n
        public void Write(string key, bool value)
        {
            string val = value ? "true" : "false";
            WriteKeyValue(key, val);
        }

        // if value then: key=value\n
        public void Write(string key, string value)
        {
            if (value.NotEmpty())
            {
                WriteKeyValue(key, value);
            }
        }

        // key=values0;values1;values2\n
        public void Write(string key, string[] values)
        {
            Write(key);
            Write('=');
            Write(values);
            Write('\n');
        }

        // values0;values1;values2
        public void Write(string[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                Write(values[i]);
                if (i != values.Length - 1)
                    Write(';');
            }
        }

        public void WriteLine()
        {
            Write('\n');
        }

        public void WriteLine(string value)
        {
            Write(value);
            Write('\n');
        }

        public void WriteLine(string[] values)
        {
            Write(values);
            Write('\n');
        }

        void WriteKeyValue(string key, string value)
        {
            if (Buffer != null)
                ByteBufferWriteKV(Buffer, key, key.Length, value, value.Length);
            else
                Managed.Append(key).Append('=').Append(value).Append('\n');
        }

        static string FloatToString(float value, int maxDecimals)
        {
            float scale = (float)Math.Pow(10, maxDecimals);
            float truncated = (float)(Math.Truncate(value * scale) / scale);
            return truncated.ToString("0." + new string('#', maxDecimals), CultureInfo.InvariantCulture);
        }

        static bool IsNativeLoadFailure(Exception e)
            => e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    }
}
