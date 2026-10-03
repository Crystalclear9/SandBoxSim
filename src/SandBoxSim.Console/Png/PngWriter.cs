namespace SandBoxSim.ConsoleApp.Png;

/// <summary>解码后的图像：8 位 RGB，行主序，无 padding。</summary>
public sealed class PngImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>RGB 数据，长度 = Width × Height × 3。</summary>
    public byte[] Pixels { get; }

    public PngImage(int width, int height)
    {
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        Pixels = new byte[Width * Height * 3];
    }

    public PngImage(int width, int height, byte[] pixels)
    {
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        Pixels = pixels ?? new byte[Width * Height * 3];
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
        int index = ((y * Width) + x) * 3;
        Pixels[index] = r;
        Pixels[index + 1] = g;
        Pixels[index + 2] = b;
    }

    public void SetPixel(int x, int y, Tui.Rgb color) => SetPixel(x, y, color.R, color.G, color.B);

    public void GetPixel(int x, int y, out byte r, out byte g, out byte b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            r = 0;
            g = 0;
            b = 0;
            return;
        }
        int index = ((y * Width) + x) * 3;
        r = Pixels[index];
        g = Pixels[index + 1];
        b = Pixels[index + 2];
    }

    /// <summary>用一个常量颜色填充整幅图。</summary>
    public void Fill(Tui.Rgb color)
    {
        for (int i = 0; i < Pixels.Length; i += 3)
        {
            Pixels[i] = color.R;
            Pixels[i + 1] = color.G;
            Pixels[i + 2] = color.B;
        }
    }
}

/// <summary>
/// PNG 编码器（仅 8 位真彩、无 alpha）。
///
/// 为什么需要它：控制台 TUI 只能"活着看"，无法在 CI 或评审时留下证据。
/// 世界快照 PNG 让"涌现性是否真的发生"变成可以贴进 PR、可以被人眼复核的产物
/// （见 docs/10-DebugAndObservation.md 与验收标准 5）。
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>编码成 PNG 字节流。</summary>
    /// <param name="image">源图。</param>
    /// <param name="filterType">行过滤器：0=None（最快），1=Sub，2=Up，3=Average，4=Paeth。</param>
    public static byte[] Encode(PngImage image, int filterType = 0)
    {
        if (image == null) { throw new System.ArgumentNullException(nameof(image)); }
        if (filterType < 0 || filterType > 4) { filterType = 0; }

        int width = image.Width;
        int height = image.Height;
        int stride = width * 3;

        // 每行前面加一个过滤器字节
        var raw = new byte[(stride + 1) * height];
        int rawIndex = 0;
        for (int y = 0; y < height; y++)
        {
            raw[rawIndex++] = (byte)filterType;
            int rowStart = y * stride;

            switch (filterType)
            {
                case 0:
                    System.Array.Copy(image.Pixels, rowStart, raw, rawIndex, stride);
                    break;

                case 1:
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = i >= 3 ? image.Pixels[rowStart + i - 3] : (byte)0;
                        raw[rawIndex + i] = (byte)(image.Pixels[rowStart + i] - left);
                    }
                    break;

                case 2:
                    for (int i = 0; i < stride; i++)
                    {
                        byte up = y > 0 ? image.Pixels[rowStart - stride + i] : (byte)0;
                        raw[rawIndex + i] = (byte)(image.Pixels[rowStart + i] - up);
                    }
                    break;

                case 3:
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = i >= 3 ? image.Pixels[rowStart + i - 3] : (byte)0;
                        byte up = y > 0 ? image.Pixels[rowStart - stride + i] : (byte)0;
                        raw[rawIndex + i] = (byte)(image.Pixels[rowStart + i] - ((left + up) >> 1));
                    }
                    break;

                default: // Paeth
                    for (int i = 0; i < stride; i++)
                    {
                        byte left = i >= 3 ? image.Pixels[rowStart + i - 3] : (byte)0;
                        byte up = y > 0 ? image.Pixels[rowStart - stride + i] : (byte)0;
                        byte upLeft = (y > 0 && i >= 3) ? image.Pixels[rowStart - stride + i - 3] : (byte)0;
                        raw[rawIndex + i] = (byte)(image.Pixels[rowStart + i] - Paeth(left, up, upLeft));
                    }
                    break;
            }

            rawIndex += stride;
        }

        byte[] compressed = Deflate.Compress(raw, DeflateContainer.Zlib);

        using var stream = new System.IO.MemoryStream(compressed.Length + 128);
        stream.Write(Signature, 0, Signature.Length);
        WriteChunk(stream, "IHDR", BuildIhdr(width, height));
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", System.Array.Empty<byte>());
        return stream.ToArray();
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;    // 位深
        ihdr[9] = 2;    // 颜色类型：2 = TrueColor (RGB)
        ihdr[10] = 0;   // 压缩方法：0 = deflate
        ihdr[11] = 0;   // 过滤方法：0
        ihdr[12] = 0;   // 隔行：0 = 无
        return ihdr;
    }

    private static void WriteChunk(System.IO.Stream stream, string type, byte[] data)
    {
        var lengthBytes = new byte[4];
        WriteBigEndian(lengthBytes, 0, (uint)data.Length);
        stream.Write(lengthBytes, 0, 4);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) { typeBytes[i] = (byte)type[i]; }
        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        var crcInput = new byte[4 + data.Length];
        System.Array.Copy(typeBytes, 0, crcInput, 0, 4);
        System.Array.Copy(data, 0, crcInput, 4, data.Length);
        uint crc = Checksums.Crc32(crcInput, 0, crcInput.Length);

        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = System.Math.Abs(p - a);
        int pb = System.Math.Abs(p - b);
        int pc = System.Math.Abs(p - c);
        if (pa <= pb && pa <= pc) { return a; }
        if (pb <= pc) { return b; }
        return c;
    }

    /// <summary>写成文件（自动建目录）。返回写入的字节数。</summary>
    public static int WriteFile(string path, PngImage image, int filterType = 0)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }
        byte[] bytes = Encode(image, filterType);
        System.IO.File.WriteAllBytes(path, bytes);
        return bytes.Length;
    }
}
