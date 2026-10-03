namespace SandBoxSim.ConsoleApp.Png;

/// <summary>
/// PNG 解码器（支持位深 8、颜色类型 0/2/4/6，非隔行）。
///
/// 存在意义：让"快照导出是否正确"成为**自动化测试**，而不是靠人看图。
/// <c>SnapshotTests.RoundTripMatchesSource</c> 用它对 PngWriter 的输出做逐像素校验。
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static PngImage Decode(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length < 8)
        {
            throw new System.InvalidOperationException("PNG 数据过短");
        }
        for (int i = 0; i < Signature.Length; i++)
        {
            if (fileBytes[i] != Signature[i])
            {
                throw new System.InvalidOperationException("PNG 签名不匹配");
            }
        }

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;
        var idat = new System.IO.MemoryStream();

        int position = 8;
        while (position + 8 <= fileBytes.Length)
        {
            int length = (int)ReadBigEndian(fileBytes, position);
            string type = System.Text.Encoding.ASCII.GetString(fileBytes, position + 4, 4);
            int dataStart = position + 8;

            if (dataStart + length + 4 > fileBytes.Length)
            {
                throw new System.InvalidOperationException("PNG chunk 长度越界：" + type);
            }

            if (type == "IHDR")
            {
                width = (int)ReadBigEndian(fileBytes, dataStart);
                height = (int)ReadBigEndian(fileBytes, dataStart + 4);
                bitDepth = fileBytes[dataStart + 8];
                colorType = fileBytes[dataStart + 9];
                interlace = fileBytes[dataStart + 12];
            }
            else if (type == "IDAT")
            {
                idat.Write(fileBytes, dataStart, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            position = dataStart + length + 4;
        }

        if (width <= 0 || height <= 0)
        {
            throw new System.InvalidOperationException("PNG 缺少有效的 IHDR");
        }
        if (bitDepth != 8)
        {
            throw new System.NotSupportedException("仅支持位深 8 的 PNG，实际：" + bitDepth);
        }
        if (interlace != 0)
        {
            throw new System.NotSupportedException("不支持隔行 PNG");
        }

        int channels = colorType switch
        {
            0 => 1,   // 灰度
            2 => 3,   // RGB
            4 => 2,   // 灰度 + alpha
            6 => 4,   // RGBA
            _ => throw new System.NotSupportedException("不支持的颜色类型：" + colorType),
        };

        byte[] compressed = idat.ToArray();
        int rawLength = (width * channels + 1) * height;
        byte[] raw = Inflater.InflateZlib(compressed, rawLength);

        if (raw.Length < rawLength)
        {
            throw new System.InvalidOperationException("PNG 像素数据不足：期望 " + rawLength + "，实际 " + raw.Length);
        }

        var image = new PngImage(width, height);
        Unfilter(raw, width, height, channels, image);
        return image;
    }

    public static PngImage ReadFile(string path) => Decode(System.IO.File.ReadAllBytes(path));

    private static void Unfilter(byte[] raw, int width, int height, int channels, PngImage image)
    {
        int stride = width * channels;
        var previous = new byte[stride];
        var current = new byte[stride];
        int rawIndex = 0;

        for (int y = 0; y < height; y++)
        {
            int filterType = raw[rawIndex++];

            for (int i = 0; i < stride; i++)
            {
                int rawByte = raw[rawIndex + i];
                int left = i >= channels ? current[i - channels] : 0;
                int up = previous[i];
                int upLeft = i >= channels ? previous[i - channels] : 0;

                int value;
                switch (filterType)
                {
                    case 0: value = rawByte; break;
                    case 1: value = rawByte + left; break;
                    case 2: value = rawByte + up; break;
                    case 3: value = rawByte + ((left + up) >> 1); break;
                    case 4: value = rawByte + Paeth((byte)left, (byte)up, (byte)upLeft); break;
                    default: throw new System.InvalidOperationException("未知的 PNG 过滤器：" + filterType);
                }

                current[i] = (byte)(value & 0xFF);
            }

            rawIndex += stride;

            // 输出到 RGB 图（忽略 alpha）
            for (int x = 0; x < width; x++)
            {
                int src = x * channels;
                byte r;
                byte g;
                byte b;
                switch (channels)
                {
                    case 1:
                        r = g = b = current[src];
                        break;
                    case 2:
                        r = g = b = current[src];
                        break;
                    case 3:
                        r = current[src];
                        g = current[src + 1];
                        b = current[src + 2];
                        break;
                    default:
                        r = current[src];
                        g = current[src + 1];
                        b = current[src + 2];
                        break;
                }
                image.SetPixel(x, y, r, g, b);
            }

            System.Array.Copy(current, previous, stride);
        }
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

    private static uint ReadBigEndian(byte[] buffer, int offset)
        => ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16)
         | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
}
