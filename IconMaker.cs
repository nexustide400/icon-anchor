// IconAnchor のアイコン (.ico) を生成するツール。
// アプリ本体とは無関係で、アイコンを描き直したいときだけ使います。
//
//   csc.exe -target:exe -out:IconMaker.exe -reference:System.Drawing.dll IconMaker.cs
//   IconMaker.exe IconAnchor.ico

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconMaker
{
    // .ico の中に入れるサイズ。用途に応じて Windows が使い分ける
    static readonly int[] SIZES = { 16, 24, 32, 48, 64, 128, 256 };

    static readonly Color BACKGROUND = ColorTranslator.FromHtml("#3b82f6");

    static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddArc(x,           y,           r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r*2, y,           r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r*2, y + h - r*2, r * 2, r * 2,   0, 90);
        p.AddArc(x,           y + h - r*2, r * 2, r * 2,  90, 90);
        p.CloseFigure();
        return p;
    }

    // 96x96 を基準に描いて、求められたサイズへ拡大縮小する
    static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = size / 96f;

            using (GraphicsPath bg = RoundedRect(0, 0, size, size, 20f * s))
            using (Brush brush = new SolidBrush(BACKGROUND))
                g.FillPath(brush, bg);

            // 小さいサイズで線が細くなりすぎないよう下限を設ける
            float width = Math.Max(7f * s, 1.6f);

            using (Pen pen = new Pen(Color.White, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap   = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                g.DrawEllipse(pen, 40f * s, 14f * s, 16f * s, 16f * s);   // 上の輪
                g.DrawLine(pen, 48f * s, 30f * s, 48f * s, 80f * s);      // 縦棒
                g.DrawLine(pen, 28f * s, 42f * s, 68f * s, 42f * s);      // 横棒
                g.DrawBezier(pen, 18f*s, 56f*s, 18f*s, 76f*s, 32f*s, 82f*s, 48f*s, 82f*s);  // 左の爪
                g.DrawBezier(pen, 48f*s, 82f*s, 64f*s, 82f*s, 78f*s, 76f*s, 78f*s, 56f*s);  // 右の爪
            }
        }
        return bmp;
    }

    static void PutInt(byte[] b, int at, int v)
    {
        b[at] = (byte)v; b[at+1] = (byte)(v >> 8); b[at+2] = (byte)(v >> 16); b[at+3] = (byte)(v >> 24);
    }

    static void PutShort(byte[] b, int at, int v)
    {
        b[at] = (byte)v; b[at+1] = (byte)(v >> 8);
    }

    // 昔ながらの DIB 形式。PNG 形式より対応環境が広い
    static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int pixels  = w * h * 4;
        int maskRow = ((w + 31) / 32) * 4;   // 1行を4バイト境界に揃える決まり
        int mask    = maskRow * h;

        byte[] buf = new byte[40 + pixels + mask];

        PutInt(buf, 0, 40);          // ヘッダのサイズ
        PutInt(buf, 4, w);
        PutInt(buf, 8, h * 2);       // 画像 + マスク の2枚ぶんと申告する決まり
        PutShort(buf, 12, 1);
        PutShort(buf, 14, 32);       // 32bit カラー
        PutInt(buf, 16, 0);          // 無圧縮
        PutInt(buf, 20, pixels + mask);

        // 画素は下の行から順に、BGRA で並べる
        int o = 40;
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = bmp.GetPixel(x, y);
                buf[o++] = c.B;
                buf[o++] = c.G;
                buf[o++] = c.R;
                buf[o++] = c.A;
            }
        }
        // マスクは全て0のまま。透明の扱いは上のアルファ値が受け持つ
        return buf;
    }

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "IconAnchor.ico";

        byte[][] images = new byte[SIZES.Length][];
        string[] kinds  = new string[SIZES.Length];

        for (int i = 0; i < SIZES.Length; i++)
        {
            using (Bitmap bmp = Draw(SIZES[i]))
            {
                if (SIZES[i] >= 128)
                {
                    // 大きいサイズは PNG で入れる（ファイルサイズ削減のため。Vista以降が対応）
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        images[i] = ms.ToArray();
                    }
                    kinds[i] = "PNG";
                }
                else
                {
                    images[i] = ToDib(bmp);
                    kinds[i] = "DIB";
                }
            }
        }

        using (FileStream fs = new FileStream(outPath, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);                 // 予約
            w.Write((short)1);                 // 1 = アイコン
            w.Write((short)SIZES.Length);      // 収録数

            int offset = 6 + 16 * SIZES.Length;
            for (int i = 0; i < SIZES.Length; i++)
            {
                byte dim = (byte)(SIZES[i] >= 256 ? 0 : SIZES[i]);   // 256 は 0 で表す決まり
                w.Write(dim);
                w.Write(dim);
                w.Write((byte)0);              // パレット数（フルカラーなので 0）
                w.Write((byte)0);              // 予約
                w.Write((short)1);             // プレーン数
                w.Write((short)32);            // 32bit カラー
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < SIZES.Length; i++)
                w.Write(images[i]);
        }

        Console.WriteLine("wrote " + outPath);
        for (int i = 0; i < SIZES.Length; i++)
            Console.WriteLine("  " + SIZES[i] + "px : " + kinds[i] + " " + images[i].Length + " bytes");
    }
}
