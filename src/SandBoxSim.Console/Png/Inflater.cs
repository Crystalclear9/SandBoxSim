namespace SandBoxSim.ConsoleApp.Png;

/// <summary>
/// 最小可用的 DEFLATE 解压器（stored / 固定 Huffman / 动态 Huffman）。
///
/// 存在的意义有两个：
///   1. PNG 往返测试需要读回自己写出的文件，才能证明"编码器没写错" ——
///      没有解码器的编码器只能靠肉眼看图，无法在 CI 里自动验证；
///   2. 报告工具未来可能要读外部 PNG（例如把地图叠在截图上）。
///
/// 实现的是 RFC 1951 的完整语义，但不追求速度（一次性分配 + 数组输出）。
/// </summary>
public sealed class Inflater
{
    private readonly byte[] _input;
    private int _bytePos;
    private int _bitBuffer;
    private int _bitCount;

    private byte[] _output;
    private int _outputLength;

    public Inflater(byte[] input)
    {
        _input = input ?? System.Array.Empty<byte>();
        _output = new byte[System.Math.Max(1024, _input.Length * 8)];
    }

    /// <summary>
    /// 解压裸 DEFLATE 流。
    /// </summary>
    /// <param name="expectedSize">预期输出长度；&gt;0 时按它预分配并校验。</param>
    public static byte[] Inflate(byte[] deflated, int expectedSize = 0)
    {
        var inflater = new Inflater(deflated);
        return inflater.Run(expectedSize);
    }

    /// <summary>解压 zlib 容器（跳过 2 字节头与 4 字节尾）。</summary>
    public static byte[] InflateZlib(byte[] zlib, int expectedSize = 0)
    {
        if (zlib == null || zlib.Length < 6)
        {
            throw new System.InvalidOperationException("zlib 数据过短");
        }
        ushort header = (ushort)((zlib[0] << 8) | zlib[1]);
        if ((header % 31) != 0)
        {
            throw new System.InvalidOperationException("zlib 头校验失败（FCHECK 不正确）");
        }

        var body = new byte[zlib.Length - 6];
        System.Array.Copy(zlib, 2, body, 0, body.Length);
        return Inflate(body, expectedSize);
    }

    public byte[] Run(int expectedSize)
    {
        if (expectedSize > 0)
        {
            _output = new byte[expectedSize + 64];
        }

        bool finalBlock = false;
        while (!finalBlock)
        {
            finalBlock = ReadBits(1) == 1;
            int blockType = ReadBits(2);

            switch (blockType)
            {
                case 0:
                    ReadStoredBlock();
                    break;
                case 1:
                    ReadHuffmanBlock(BuildFixedLiteralTree(), BuildFixedDistanceTree());
                    break;
                case 2:
                    ReadDynamicBlock();
                    break;
                default:
                    throw new System.InvalidOperationException("DEFLATE 块类型 3 非法");
            }
        }

        var result = new byte[_outputLength];
        System.Array.Copy(_output, result, _outputLength);
        return result;
    }

    // ---- 位读取 ----

    private int ReadBits(int count)
    {
        while (_bitCount < count)
        {
            if (_bytePos >= _input.Length)
            {
                // 允许在流末尾补零（某些编码器的对齐字节缺失）
                _bitBuffer |= 0 << _bitCount;
                _bitCount += 8;
                continue;
            }
            _bitBuffer |= _input[_bytePos++] << _bitCount;
            _bitCount += 8;
        }

        int mask = (1 << count) - 1;
        int value = _bitBuffer & mask;
        _bitBuffer >>= count;
        _bitCount -= count;
        return value;
    }

    private void AlignToByte()
    {
        int drop = _bitCount % 8;
        _bitBuffer >>= drop;
        _bitCount -= drop;
    }

    private int ReadByteAligned()
    {
        AlignToByte();
        if (_bitCount >= 8)
        {
            int value = _bitBuffer & 0xFF;
            _bitBuffer >>= 8;
            _bitCount -= 8;
            return value;
        }
        return _bytePos < _input.Length ? _input[_bytePos++] : 0;
    }

    // ---- 输出 ----

    private void EnsureOutput(int extra)
    {
        if (_outputLength + extra <= _output.Length) { return; }
        int newSize = _output.Length * 2 + extra + 64;
        System.Array.Resize(ref _output, newSize);
    }

    private void Append(byte value)
    {
        EnsureOutput(1);
        _output[_outputLength++] = value;
    }

    private void CopyFromHistory(int distance, int length)
    {
        if (distance <= 0 || distance > _outputLength)
        {
            throw new System.InvalidOperationException("DEFLATE 距离超出历史窗口：" + distance);
        }
        EnsureOutput(length);

        int src = _outputLength - distance;
        for (int i = 0; i < length; i++)
        {
            _output[_outputLength++] = _output[src + i];
        }
    }

    // ---- 块类型 0：存储块 ----

    private void ReadStoredBlock()
    {
        AlignToByte();
        int len = ReadByteAligned();
        len |= ReadByteAligned() << 8;
        int nlen = ReadByteAligned();
        nlen |= ReadByteAligned() << 8;

        if ((len ^ 0xFFFF) != nlen)
        {
            throw new System.InvalidOperationException("存储块的 LEN/NLEN 校验失败");
        }

        for (int i = 0; i < len; i++)
        {
            Append((byte)ReadByteAligned());
        }
    }

    // ---- Huffman 表 ----

    private sealed class HuffmanTree
    {
        public int[] Counts = System.Array.Empty<int>();
        public int[] Symbols = System.Array.Empty<int>();
        public int MaxBits;
    }

    private static HuffmanTree BuildTree(int[] codeLengths, int maxAllowedBits)
    {
        var tree = new HuffmanTree();
        int maxBits = 0;
        for (int i = 0; i < codeLengths.Length; i++)
        {
            if (codeLengths[i] > maxBits) { maxBits = codeLengths[i]; }
        }
        if (maxBits == 0) { maxBits = 1; }
        if (maxBits > maxAllowedBits)
        {
            throw new System.InvalidOperationException("Huffman 码长超过规定上限");
        }
        tree.MaxBits = maxBits;

        tree.Counts = new int[maxBits + 1];
        for (int i = 0; i < codeLengths.Length; i++)
        {
            tree.Counts[codeLengths[i]]++;
        }
        tree.Counts[0] = 0;

        var offsets = new int[maxBits + 2];
        for (int bits = 1; bits <= maxBits; bits++)
        {
            offsets[bits + 1] = offsets[bits] + tree.Counts[bits];
        }

        tree.Symbols = new int[codeLengths.Length];
        for (int symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int len = codeLengths[symbol];
            if (len == 0) { continue; }
            tree.Symbols[offsets[len]++] = symbol;
        }
        return tree;
    }

    private int DecodeSymbol(HuffmanTree tree)
    {
        int code = 0;
        int first = 0;
        int index = 0;

        for (int len = 1; len <= tree.MaxBits; len++)
        {
            code |= ReadBits(1);
            int count = tree.Counts[len];
            if (code - first < count)
            {
                return tree.Symbols[index + (code - first)];
            }
            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }

        throw new System.InvalidOperationException("Huffman 码无法解码（码表不完整）");
    }

    private static HuffmanTree BuildFixedLiteralTree()
    {
        // RFC 1951：字面量/长度码 0-143 是 8 位，144-255 是 9 位，256-279 是 7 位，280-287 是 8 位。
        var lengths = new int[288];
        for (int i = 0; i < 144; i++) { lengths[i] = 8; }
        for (int i = 144; i < 256; i++) { lengths[i] = 9; }
        for (int i = 256; i < 280; i++) { lengths[i] = 7; }
        for (int i = 280; i < 288; i++) { lengths[i] = 8; }
        return BuildTree(lengths, 15);
    }

    private static HuffmanTree BuildFixedDistanceTree()
    {
        var lengths = new int[30];
        for (int i = 0; i < 30; i++) { lengths[i] = 5; }
        return BuildTree(lengths, 15);
    }

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

    private static readonly int[] CodeLengthOrder =
    {
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15,
    };

    private void ReadHuffmanBlock(HuffmanTree literalTree, HuffmanTree distanceTree)
    {
        while (true)
        {
            int symbol = DecodeSymbol(literalTree);

            if (symbol < 256)
            {
                Append((byte)symbol);
                continue;
            }
            if (symbol == 256)
            {
                return;   // 块结束
            }

            int lengthIndex = symbol - 257;
            if (lengthIndex < 0 || lengthIndex >= LengthBase.Length)
            {
                throw new System.InvalidOperationException("长度码非法：" + symbol);
            }

            int length = LengthBase[lengthIndex];
            int extraLengthBits = LengthExtra[lengthIndex];
            if (extraLengthBits > 0) { length += ReadBits(extraLengthBits); }

            int distanceSymbol = DecodeSymbol(distanceTree);
            if (distanceSymbol >= DistanceBase.Length)
            {
                throw new System.InvalidOperationException("距离码非法：" + distanceSymbol);
            }

            int distance = DistanceBase[distanceSymbol];
            int extraDistanceBits = DistanceExtra[distanceSymbol];
            if (extraDistanceBits > 0) { distance += ReadBits(extraDistanceBits); }

            CopyFromHistory(distance, length);
        }
    }

    private void ReadDynamicBlock()
    {
        int hlit = ReadBits(5) + 257;
        int hdist = ReadBits(5) + 1;
        int hclen = ReadBits(4) + 4;

        var codeLengthLengths = new int[19];
        for (int i = 0; i < hclen; i++)
        {
            codeLengthLengths[CodeLengthOrder[i]] = ReadBits(3);
        }

        HuffmanTree codeLengthTree = BuildTree(codeLengthLengths, 7);

        int total = hlit + hdist;
        var lengths = new int[total];
        int index = 0;
        while (index < total)
        {
            int symbol = DecodeSymbol(codeLengthTree);

            if (symbol < 16)
            {
                lengths[index++] = symbol;
                continue;
            }

            int repeat;
            int value = 0;
            if (symbol == 16)
            {
                if (index == 0)
                {
                    throw new System.InvalidOperationException("码长重复码出现在开头");
                }
                repeat = ReadBits(2) + 3;
                value = lengths[index - 1];
            }
            else if (symbol == 17)
            {
                repeat = ReadBits(3) + 3;
            }
            else
            {
                repeat = ReadBits(7) + 11;
            }

            for (int i = 0; i < repeat && index < total; i++)
            {
                lengths[index++] = value;
            }
        }

        var literalLengths = new int[hlit];
        System.Array.Copy(lengths, 0, literalLengths, 0, hlit);

        var distanceLengths = new int[hdist];
        System.Array.Copy(lengths, hlit, distanceLengths, 0, hdist);

        HuffmanTree literalTree = BuildTree(literalLengths, 15);
        HuffmanTree distanceTree = BuildTree(distanceLengths, 15);

        ReadHuffmanBlock(literalTree, distanceTree);
    }
}
