using SkiaSharp;
using System.Text;
using System.Drawing;
using Rubedo.Compiler.Util;

namespace Rubedo.Compiler.ContentBuilders.SpriteBuilder;

/// <summary>
/// Packs a list of textures into a single texture.
/// </summary>
public static class TexturePacker
{
    public class Config()
    {
        public string OutputAtlasFile;
        public string OutputMapFile;
        public int MaxSize = Constants.MAX_SHEET_SIZE;
        public int Padding = Constants.DEFAULT_PADDING;
        public List<string> InputPaths;
        public bool Trim = true;
    }

    public static void Generate(Config config)
    {
        if (!Lib.Math.IsPowerOf2(config.MaxSize))
            throw new ArgumentException($"MaxSize is not a power of 2! (It's {config.MaxSize})");

        //Load sprites and figure out sprite packing.
        List<SRect> rects = new List<SRect>();
        for (int i = 0; i < config.InputPaths.Count; i++)
        {
            SKBitmap texture = SKBitmap.FromImage(SKImage.FromEncodedData(config.InputPaths[i]));
            SKRect rect;
            if (config.Trim)
                rect = Trim(texture);
            else
                rect = new SKRect(0, 0, texture.Width, texture.Height);
            rects.Add(new SRect(texture, config.InputPaths[i], config.Padding, rect));
        }
        rects.Sort((a, b) => a.Area.CompareTo(b.Area));

        RectPacker packer = new RectPacker(config.MaxSize, config.MaxSize);

        //RectanglePacker packer = new RectanglePacker(config.MaxSize, config.MaxSize);

        foreach (var rect in rects)
        {
            Point pos = packer.Pack(rect.paddedAreaWidth, rect.paddedAreaHeight);
            rect.x = pos.X; rect.y = pos.Y;

            //if (!packer.Pack(rect.paddedAreaWidth, rect.paddedAreaHeight, out rect.x, out rect.y))
            //    throw new Exception("Uh oh, we couldn't pack the rectangle :("); //TODO: Better error handling for overpacked sprites. Put into second atlas, perhaps?
        }
        //Determine final image size.
        int pngSizeX = Lib.Math.Power2Roundup(packer.PackedAreaWidth);
        int pngSizeY = Lib.Math.Power2Roundup(packer.PackedAreaHeight);

        //Create output image.
        SKBitmap atlas = new SKBitmap(pngSizeX, pngSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);

        SKCanvas canvas = new SKCanvas(atlas);
        StringBuilder atlasMap = new StringBuilder();
        int sheet = 0; //TODO: Implement multi-image atlases.
        for (int i = 0; i < rects.Count; i++)
        {
            SRect rect = rects[i];
            canvas.DrawBitmap(rect.sprite, rect.rect,
                new SKRect(rect.x, rect.y, rect.x + rect.SpriteWidth, rect.y + rect.SpriteHeight));

            int pivotX = Lib.Math.CeilToInt(0.5f * rect.SpriteWidth - rect.rect.Left);
            int pivotY = Lib.Math.CeilToInt(0.5f * rect.SpriteHeight - rect.rect.Top);
            atlasMap.Append($"{Path.GetFileName(rect.spritePath)},{sheet},{rect.x}," +
                $"{rect.y},{rect.SpriteWidth},{rect.SpriteHeight},{pivotX},{pivotY}\n");

            rect.sprite.Dispose();
        }
        canvas.Dispose();

        if (!File.Exists(config.OutputAtlasFile))
            File.Create(config.OutputAtlasFile).Close();


        using (StreamWriter stream = new StreamWriter(config.OutputAtlasFile))
            atlas.Encode(stream.BaseStream, SKEncodedImageFormat.Png, 100);

        atlas.Dispose();

        if (!File.Exists(config.OutputMapFile))
            File.Create(config.OutputMapFile).Close();


        using (StreamWriter stream = new StreamWriter(config.OutputMapFile))
            stream.Write(atlasMap);
    }

    private static SKRect Trim(SKBitmap sprite)
    {
        //values are reversed so that we can minimize and maximize values.
        SKRect ret = new SKRect(sprite.Width, sprite.Height, 0, 0);
        SKPixmap map = sprite.PeekPixels();
        //start from top left corner.
        for (int y = 0; y < sprite.Height; y++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                float alpha = map.GetPixelAlpha(x, y);
                if (alpha != 0)
                {
                    //set min and max values.
                    if (ret.Left > x)
                        ret.Left = x;
                    if (ret.Top > y)
                        ret.Top = y;
                    if (ret.Right < x)
                        ret.Right = x;
                    if (ret.Bottom < y)
                        ret.Bottom = y;
                }
            }
        }
        return ret;
    }

    public class SRect
    {
        public SKBitmap sprite;
        public string spritePath;
        public int x, y, paddedAreaWidth, paddedAreaHeight;
        public SKRect rect;

        public SRect(SKBitmap sprite, string spritePath, int padding, SKRect rect)
        {
            this.sprite = sprite;
            this.spritePath = spritePath;
            x = 0;
            y = 0;
            this.rect = rect;
            paddedAreaWidth = SpriteWidth + padding;     // pixel padding on bottom and right
            paddedAreaHeight = SpriteHeight + padding;   // all rects have same padding, so they share.
        }

        public int SpriteWidth => (int)(rect.Right - rect.Left);
        public int SpriteHeight => (int)(rect.Bottom - rect.Top);
        public int Area => paddedAreaWidth * paddedAreaHeight;
    }

    /// <summary>
    /// This is pretty naive, but it gets the job done. We pack the sprites in order largest to smallest,
    /// and try to fit them into the largest boxes they can go into.
    /// </summary>
    public class RectanglePacker
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        List<FreeSpace> nodes = new List<FreeSpace>();

        public RectanglePacker(int maxWidth = int.MaxValue, int maxHeight = int.MaxValue)
        {
            nodes.Add(new FreeSpace(0, 0, maxWidth, maxHeight));
        }

        public bool TryPack(int w, int h, out int x, out int y)
        {
            for (int i = 0; i < nodes.Count; ++i)
            {
                if (w <= nodes[i].W && h <= nodes[i].H)
                {
                    var node = nodes[i];
                    nodes.RemoveAt(i);
                    x = node.X;
                    y = node.Y;
                    int r = x + w;
                    int b = y + h;
                    nodes.Add(new FreeSpace(r, y, node.Right - r, h));
                    nodes.Add(new FreeSpace(x, b, w, node.Bottom - b));
                    nodes.Add(new FreeSpace(r, b, node.Right - r, node.Bottom - b));
                    Width = Math.Max(Width, r);
                    Height = Math.Max(Height, b);
                    return true;
                }
            }
            nodes.Sort((a, b) => a.Area.CompareTo(b.Area));
            x = 0;
            y = 0;
            return false;
        }

        public struct FreeSpace
        {
            public int X;
            public int Y;
            public int W;
            public int H;

            public int Area => W * H;

            public FreeSpace(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public int Right
            {
                get { return X + W; }
            }

            public int Bottom
            {
                get { return Y + H; }
            }
        }
    }
}