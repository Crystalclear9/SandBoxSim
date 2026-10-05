namespace SandBoxSim.ConsoleApp.Png;

/// <summary>
/// 校验和：PNG 用 CRC-32，zlib 用 Adler-32。
/// 两者都必须手写：本仓库不使用任何 NuGet 包（见 docs/build.md 的构建通道说明），
/// 而 System.IO.Compression 的 DeflateStream 无法直接产出 zlib 容器。
/// </summary>
public static class Checksums
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    /// <summary>CRC-32（IEEE 802.3），PNG chunk 校验用。</summary>
    public static uint Crc32(byte[] data, int offset, int length)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = offset; i < offset + length; i++)
        {
            c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }

    public static uint Crc32(byte[] data) => Crc32(data, 0, data.Length);

    /// <summary>Adler-32，zlib 容器校验用。</summary>
    public static uint Adler32(byte[] data, int offset, int length)
    {
        const uint Mod = 65521u;
        uint a = 1;
        uint b = 0;

        // 每 5552 字节取一次模，避免 32 位溢出（zlib 官方推荐的分块长度）。
        int remaining = length;
        int index = offset;
        while (remaining > 0)
        {
            int block = remaining < 5552 ? remaining : 5552;
            remaining -= block;
            for (int i = 0; i < block; i++)
            {
                a += data[index++];
                b += a;
            }
            a %= Mod;
            b %= Mod;
        }

        return (b << 16) | a;
    }
}
