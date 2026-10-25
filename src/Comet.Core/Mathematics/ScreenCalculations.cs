namespace Comet.Core.Mathematics
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;

    /// <summary>
    /// Retail Conquer Online 2D isometric tile distance, 8-sector compass direction,
    /// and Bresenham / DDA line-of-sight tile raycast calculations.
    /// </summary>
    public static class ScreenCalculations
    {
        /// <summary>
        /// Returns Chebyshev tile distance (used by Conquer Online for the 18x18 screen
        /// view box and jump distance checks).
        /// </summary>
        public static int GetDistance(int x1, int y1, int x2, int y2) =>
            Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

        /// <summary>
        /// Returns Euclidean distance between two map coordinates.
        /// </summary>
        public static double GetEuclideanDistance(int x1, int y1, int x2, int y2) =>
            Math.Sqrt(((x1 - x2) * (x1 - x2)) + ((y1 - y2) * (y1 - y2)));

        /// <summary>
        /// Computes the 8-way Conquer compass direction (0..7) from (x0, y0) to (x1, y1),
        /// matching WalkXCoords = { 0, -1, -1, -1, 0, 1, 1, 1 } and
        /// WalkYCoords = { 1, 1, 0, -1, -1, -1, 0, 1 }.
        /// </summary>
        public static byte GetDirectionSector(int x0, int y0, int x1, int y1)
        {
            int dir = 0;
            int[] tan = { -241, -41, 41, 241 };
            int deltaX = x1 - x0;
            int deltaY = y1 - y0;

            if (deltaX == 0)
            {
                dir = deltaY > 0 ? 0 : 4;
            }
            else if (deltaY == 0)
            {
                dir = deltaX > 0 ? 6 : 2;
            }
            else
            {
                int flag = Math.Abs(deltaX) / deltaX;
                int tempY = deltaY * 100 * flag;
                int i;
                for (i = 0; i < 4; i++)
                {
                    if (tempY < tan[i] * Math.Abs(deltaX))
                        break;
                }

                if (deltaX > 0)
                {
                    if (i == 0) dir = 5;
                    else if (i == 1) dir = 6;
                    else if (i == 2) dir = 6;
                    else if (i == 3) dir = 7;
                    else dir = 0;
                }
                else
                {
                    if (i == 0) dir = 1;
                    else if (i == 1) dir = 2;
                    else if (i == 2) dir = 2;
                    else if (i == 3) dir = 3;
                    else dir = 4;
                }
            }

            return (byte)((dir + 8) % 8);
        }

        /// <summary>
        /// Traces all tiles between (x0, y0) and (x1, y1) using Bresenham's line algorithm
        /// (used for jump elevation checks and ranged attack line-of-sight).
        /// </summary>
        public static List<Point> BresenhamLine(int x0, int y0, int x1, int y1)
        {
            var points = new List<Point>();
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                points.Add(new Point(x0, y0));
                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            return points;
        }
    }
}
