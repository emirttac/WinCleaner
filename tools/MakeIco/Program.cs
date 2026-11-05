using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MakeIco <input.png> <output.ico>");
            return 1;
        }

        Build(args[0], args[1]);
        Console.WriteLine("Wrote " + args[1]);
        return 0;
    }

    static void Build(string pngPath, string icoPath)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        using (var src = new Bitmap(pngPath))
        {
            var images = new List<Bitmap>();
            var pngBlobs = new List<byte[]>();

            foreach (var s in sizes)
            {
                var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, s, s));
                }
                images.Add(bmp);

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngBlobs.Add(ms.ToArray());
                }
            }

            using (var fs = new FileStream(icoPath, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)images.Count);

                int offset = 6 + (16 * images.Count);
                for (int i = 0; i < images.Count; i++)
                {
                    var bmp = images[i];
                    var blob = pngBlobs[i];
                    bw.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));
                    bw.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write(blob.Length);
                    bw.Write(offset);
                    offset += blob.Length;
                }

                foreach (var blob in pngBlobs)
                    bw.Write(blob);
            }

            foreach (var bmp in images)
                bmp.Dispose();
        }
    }
}
