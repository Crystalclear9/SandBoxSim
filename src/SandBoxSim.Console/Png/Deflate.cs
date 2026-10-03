namespace SandBoxSim.ConsoleApp.Png;

/// <summary>DEFLATE 输出容器。</summary>
public enum DeflateContainer
{
    /// <summary>裸 DEFLATE 流（PNG 的 IDAT 用这个）。</summary>
    Raw = 0,

    /// <summary>zlib 容器（2 字节头 + DEFLATE + 4 字节 Adler-32）。.NET 的 DeflateStream 能直接读。</summary>
    Zlib = 1,
}

/// <summary>
/// 最小可用的 DEFLATE 压缩器（固定 Huffman + 哈希链 LZ77）。
///
/// 为什么自己写：
///   1. 仓库要求"无 NuGet 也能构建"；
///   2. PNG 导出需要 zlib 容器，而 BCL 的 DeflateStream 只给裸 DEFLATE；
///   3. 这个类总共不到 300 行，逻辑封闭、易验证（配 PNG 往返测试）。
///
/// 为什么用固定 Huffman 而不是动态 Huffman：动态码表能把压缩率再提高 5~10%，
/// 但需要建两棵 Huffman 树 + 码长限制算法，复杂度显著上升；
/// 世界快照是几百 KB 的开发期产物，固定 Huffman 完全够用。
/// </summary>
public static class Deflate
{
    private const int WindowSize = 32768;
    private const int MinMatch = 3;
    private const int MaxMatch = 258;
    private const int HashBits = 15;
    private const int HashSize = 1 << HashBits;
    private const int MaxChain = 32;

    /// <summary>压缩字节数组。</summary>
    public static byte[] Compress(byte[] data, DeflateContainer container = DeflateContainer.Raw)
    {
        var bitWriter = new BitWriter(data.Length + 64);

        // 单个 block，BFINAL=1，BTYPE=01（固定 Huffman）
        bitWriter.WriteBits(1, 1);
        bitWriter.WriteBits(1, 2);

        int length = data.Length;
        int[] head = new int[HashSize];
        int[] prev = new int[WindowSize];
        for (int i = 0; i < HashSize; i++) { head[i] = -1; }
        for (int i = 0; i < WindowSize; i++) { prev[i] = -1; }

        int position = 0;
        while (position < length)
        {
            int matchLength = 0;
            int matchDistance = 0;

            if (position + MinMatch <= length)
            {
                int hash = Hash(data, position);
                int candidate = head[hash];
                int chainSteps = 0;
                int limit = position - WindowSize;
                if (limit < 0) { limit = 0; }

                while (candidate >= limit && candidate >= 0 && chainSteps < MaxChain)
                {
                    int len = 0;
                    int maxLen = System.Math.Min(MaxMatch, length - position);
                    while (len < maxLen && data[candidate + len] == data[position + len]) { len++; }

                    if (len > matchLength)
                    {
                        matchLength = len;
                        matchDistance = position - candidate;
                        if (len >= MaxMatch) { break; }
                    }

                    candidate = prev[candidate & (WindowSize - 1)];
                    chainSteps++;
                }
            }

            if (matchLength >= MinMatch)
            {
                WriteMatch(bitWriter, matchLength, matchDistance);

                // 匹配覆盖的每个位置都要插入哈希链，否则后续匹配质量会迅速下降。
                for (int i = 0; i < matchLength; i++)
                {
                    if (position + i + MinMatch <= length)
                    {
                        int h = Hash(data, position + i);
                        prev[(position + i) & (WindowSize - 1)] = head[h];
                        head[h] = position + i;
                    }
                }
                position += matchLength;
            }
            else
            {
                WriteLiteral(bitWriter, data[position]);
                if (position + MinMatch <= length)
                {
                    int h = Hash(data, position);
                    prev[position & (WindowSize - 1)] = head[h];
                    head[h] = position;
                }
                position++;
            }
        }

        WriteSymbol(bitWriter, 256);   // end-of-block
        bitWriter.AlignToByte();

        byte[] deflated = bitWriter.ToArray();

        if (container == DeflateContainer.Raw) { return deflated; }

        // zlib 容器：CMF=0x78（deflate, 32K 窗口），FLG 使 (CMF<<8|FLG) % 31 == 0
        var zlib = new byte[deflated.Length + 6];
        zlib[0] = 0x78;
        zlib[1] = 0x9C;   // 默认压缩级别
        System.Array.Copy(deflated, 0, zlib, 2, deflated.Length);
        uint adler = Checksums.Adler32(data, 0, data.Length);
        zlib[deflated.Length + 2] = (byte)(adler >> 24);
        zlib[deflated.Length + 3] = (byte)(adler >> 16);
        zlib[deflated.Length + 4] = (byte)(adler >> 8);
        zlib[deflated.Length + 5] = (byte)adler;
        return zlib;
    }

    private static int Hash(byte[] data, int position)
    {
        // 经典 3 字节哈希：位混合避免相邻串撞进同一桶。
        uint h = data[position];
        h = (h << 5) ^ data[position + 1];
        h = (h << 5) ^ data[position + 2];
        return (int)(h & (HashSize - 1));
    }

    // ---- 位写入器 ----

    private sealed class BitWriter
    {
        private byte[] _buffer;
        private int _byteCount;
        private int _bitBuffer;
        private int _bitCount;

        public BitWriter(int capacity)
        {
            _buffer = new byte[capacity];
            _byteCount = 0;
            _bitBuffer = 0;
            _bitCount = 0;
        }

        public void WriteBits(int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _bitBuffer |= ((value >> i) & 1) << _bitCount;
                _bitCount++;
                if (_bitCount == 8)
                {
                    EnsureCapacity(1);
                    _buffer[_byteCount++] = (byte)_bitBuffer;
                    _bitBuffer = 0;
                    _bitCount = 0;
                }
            }
        }

        public void WriteBitsReversed(int value, int count)
        {
            // Huffman 码在 DEFLATE 里是按位反序写入的。
            for (int i = count - 1; i >= 0; i--)
            {
                WriteBits((value >> i) & 1, 1);
            }
        }

        public void AlignToByte()
        {
            if (_bitCount > 0)
            {
                EnsureCapacity(1);
                _buffer[_byteCount++] = (byte)_bitBuffer;
                _bitBuffer = 0;
                _bitCount = 0;
            }
        }

        public byte[] ToArray()
        {
            var result = new byte[_byteCount];
            System.Array.Copy(_buffer, result, _byteCount);
            return result;
        }

        private void EnsureCapacity(int extra)
        {
            if (_byteCount + extra <= _buffer.Length) { return; }
            int newSize = _buffer.Length * 2 + 64;
            System.Array.Resize(ref _buffer, newSize);
        }
    }

    // ---- 固定 Huffman 码表（RFC 1951 3.2.6） ----

    private static void WriteSymbol(BitWriter writer, int symbol)
    {
        if (symbol <= 143)
        {
            writer.WriteBitsReversed(0x30 + symbol, 8);
        }
        else if (symbol <= 255)
        {
            writer.WriteBitsReversed(0x190 + (symbol - 144), 9);
        }
        else if (symbol <= 279)
        {
            writer.WriteBitsReversed(symbol - 256, 7);
        }
        else
        {
            writer.WriteBitsReversed(0xC0 + (symbol - 280), 8);
        }
    }

    private static void WriteLiteral(BitWriter writer, byte value) => WriteSymbol(writer, value);

    private static readonly int[] LengthBase =
    {
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
    };

    private static readonly int[] LengthExtra =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
    };

    private static readonly int[] DistanceBase =
    {
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577,
    };

    private static readonly int[] DistanceExtra =
    {
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
    };

    private static void WriteMatch(BitWriter writer, int length, int distance)
    {
        // 长度码：257..285
        int lengthIndex = 0;
        for (int i = LengthBase.Length - 1; i >= 0; i--)
        {
            if (length >= LengthBase[i])
            {
                lengthIndex = i;
                break;
            }
        }

        int lengthSymbol = 257 + lengthIndex;
        WriteSymbol(writer, lengthSymbol);
        int extraLengthBits = LengthExtra[lengthIndex];
        if (extraLengthBits > 0)
        {
            writer.WriteBits(length - LengthBase[lengthIndex], extraLengthBits);
        }

        // 距离码：0..29，距离码本身也是反序写入的 5 位固定码
        int distanceIndex = 0;
        for (int i = DistanceBase.Length - 1; i >= 0; i--)
        {
            if (distance >= DistanceBase[i])
            {
                distanceIndex = i;
                break;
            }
        }

        writer.WriteBitsReversed(distanceIndex, 5);
        int extraDistanceBits = DistanceExtra[distanceIndex];
        if (extraDistanceBits > 0)
        {
            writer.WriteBits(distance - DistanceBase[distanceIndex], extraDistanceBits);
        }
    }
}
