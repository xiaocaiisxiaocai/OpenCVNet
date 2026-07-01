using System;

namespace VisionInspection.Core.Models
{
    /// <summary>
    /// 工位 ROI 矩形（图像像素坐标，位于配方“标定坐标系”；运行时经定位配准映射到当前图像）。
    /// </summary>
    public struct RoiRect : IEquatable<RoiRect>
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public RoiRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int Area => Width * Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

        public bool Equals(RoiRect other) =>
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is RoiRect r && Equals(r);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + X;
                h = h * 31 + Y;
                h = h * 31 + Width;
                h = h * 31 + Height;
                return h;
            }
        }

        public override string ToString() => $"[{X},{Y},{Width}x{Height}]";
    }
}
