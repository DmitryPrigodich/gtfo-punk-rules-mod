using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// Minimal PNG reader (8-bit RGBA or RGB, not interlaced) — the game ships without Unity's image loading module.
/// Enough for the mod's own icons. Returns null on anything it doesn't understand.
/// </summary>
public static class Png
{
    public static Texture2D Load(string path)
    {
        try { return Decode(File.ReadAllBytes(path)); }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Png] {Path.GetFileName(path)}: {e.Message}"); return null; }
    }

    private static int BE(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

    public static Texture2D Decode(byte[] d)
    {
        var rgba = DecodeRgba(d, out int w, out int h);
        // PNG rows go top-down, Unity textures bottom-up.
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4;
                px[(h - 1 - y) * w + x] = new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px);
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return tex;
    }

    /// <summary>RGBA bytes, rows top-down.</summary>
    public static byte[] DecodeRgba(byte[] d, out int w, out int h)
    {
        if (d.Length < 33 || d[1] != 'P' || d[2] != 'N' || d[3] != 'G') throw new Exception("not a PNG");
        w = 0; h = 0; int colorType = 0;
        var idat = new MemoryStream();
        for (int p = 8; p + 8 <= d.Length;)
        {
            int len = BE(d, p);
            string type = System.Text.Encoding.ASCII.GetString(d, p + 4, 4);
            int data = p + 8;
            if (type == "IHDR")
            {
                w = BE(d, data); h = BE(d, data + 4);
                int depth = d[data + 8]; colorType = d[data + 9]; int interlace = d[data + 12];
                if (depth != 8 || (colorType != 6 && colorType != 2) || interlace != 0) throw new Exception($"unsupported PNG (depth {depth}, color {colorType}, interlace {interlace})");
            }
            else if (type == "IDAT") idat.Write(d, data, len);
            else if (type == "IEND") break;
            p = data + len + 4; // + CRC
        }
        int bpp = colorType == 6 ? 4 : 3, stride = w * bpp;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length) { int n = z.Read(raw, read, raw.Length - read); if (n <= 0) break; read += n; }
        }

        // Undo the per-row filters.
        var img = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)], src = y * (stride + 1) + 1, row = y * stride, prev = row - stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? img[row + x - bpp] : 0;
                int b = y > 0 ? img[prev + x] : 0;
                int c = x >= bpp && y > 0 ? img[prev + x - bpp] : 0;
                int v = raw[src + x];
                v += f switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                img[row + x] = (byte)v;
            }
        }

        if (bpp == 4) return img;
        var rgba = new byte[w * h * 4];
        for (int i = 0, j = 0; i < img.Length; i += 3, j += 4) { rgba[j] = img[i]; rgba[j + 1] = img[i + 1]; rgba[j + 2] = img[i + 2]; rgba[j + 3] = 255; }
        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
