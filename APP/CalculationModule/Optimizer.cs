using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using OpenCvSharp;
using Point = NetTopologySuite.Geometries.Point;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        private double[,] CalculateDifferenceMap(double[][] population, List<Point> existingBins, double cellSize)
        {
            int width = population.Length;
            int height = population[0].Length;
            var trashbinHeatmap = new double[width, height];

            foreach (var bin in existingBins)
            {
                int x = (int)(bin.X / cellSize);
                int y = (int)(bin.Y / cellSize);
                if (x >= 0 && x < width && y >= 0 && y < height)
                    trashbinHeatmap[x, y] += 1;
            }

            trashbinHeatmap = ApplyGaussianBlur(trashbinHeatmap, kernelSize: 7, sigma: 1.5);

            var difference = new double[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    difference[x, y] = population[x][y] - trashbinHeatmap[x, y];

            return difference;
        }

        private List<Point> FindPeaks(double[,] map, double cellSize, int count, double minDistance, Polygon polygon = null)
        {
            var peaks = new List<Point>();
            int minDistInCells = (int)(minDistance / cellSize);

            for (int i = 0; i < count; i++)
            {
                double maxVal = 0;
                int maxX = 0, maxY = 0;


                for (int x = 0; x < map.GetLength(0); x++)
                {
                    for (int y = 0; y < map.GetLength(1); y++)
                    {
                        if (map[x, y] > maxVal)
                        {
                            var point = new Point(
                                x * cellSize + cellSize / 2,
                                y * cellSize + cellSize / 2);


                            if (polygon != null && !polygon.Contains(point))
                                continue;

                            maxVal = map[x, y];
                            maxX = x;
                            maxY = y;
                        }
                    }
                }

                if (maxVal <= 0)
                    break;

                var peakPoint = new Point(
                    maxX * cellSize + cellSize / 2,
                    maxY * cellSize + cellSize / 2);
                peaks.Add(peakPoint);


                for (int x = Math.Max(0, maxX - minDistInCells);
                     x <= Math.Min(map.GetLength(0) - 1, maxX + minDistInCells); x++)
                {
                    for (int y = Math.Max(0, maxY - minDistInCells);
                         y <= Math.Min(map.GetLength(1) - 1, maxY + minDistInCells); y++)
                    {
                        var currentPoint = new Point(
                            x * cellSize + cellSize / 2,
                            y * cellSize + cellSize / 2);

                        if (polygon == null || polygon.Contains(currentPoint))
                        {
                            map[x, y] = 0;
                        }
                    }
                }
            }

            return peaks;
        }

        private double[,] ApplyGaussianBlur(double[,] input, int kernelSize, double sigma)
        {
            int width = input.GetLength(0);
            int height = input.GetLength(1);

            using (Mat src = new Mat(height, width, MatType.CV_64FC1))
            using (Mat dst = new Mat())
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        src.Set(y, x, input[x, y]);
                    }
                }

                Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), sigma);

                var output = new double[width, height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        output[x, y] = dst.Get<double>(y, x);
                    }
                }

                return output;
            }
        }

        public List<Point> Optimize(double[][] population, List<Point> existingBins,
                          double cellSize, int binCount, double minDistance,
                          Polygon polygon = null)
        {
            var allBins = new HashSet<Point>(existingBins, new PointComparer());
            var newBins = new List<Point>();

            for (int i = 0; i < binCount; i++)
            {
                var differenceMap = CalculateDifferenceMap(population, allBins.ToList(), cellSize);
                var candidates = FindPeaks(differenceMap, cellSize, 3, minDistance, polygon);

                Point? bestCandidate = null;
                foreach (var candidate in candidates)
                {
                    bool isDuplicate = allBins.Any(b =>
                        Math.Abs(b.X - candidate.X) < 0.0001 &&
                        Math.Abs(b.Y - candidate.Y) < 0.0001);

                    
                    bool isValid = !allBins.Any(b => b.Distance(candidate) < minDistance);

                    if (!isDuplicate && isValid)
                    {
                        bestCandidate = candidate;
                        break;
                    }
                }

                if (bestCandidate == null) break;

                newBins.Add(bestCandidate);
                allBins.Add(bestCandidate);

                
                ZeroOutArea(differenceMap, bestCandidate, cellSize, minDistance);
            }

            return newBins;
        }

        private void ZeroOutArea(double[,] map, Point center, double cellSize, double radius)
        {
            int centerX = (int)(center.X / cellSize);
            int centerY = (int)(center.Y / cellSize);
            int radiusInCells = (int)(radius / cellSize) + 1;

            for (int x = Math.Max(0, centerX - radiusInCells);
                 x <= Math.Min(map.GetLength(0) - 1, centerX + radiusInCells); x++)
            {
                for (int y = Math.Max(0, centerY - radiusInCells);
                     y <= Math.Min(map.GetLength(1) - 1, centerY + radiusInCells); y++)
                {
                    var point = new Point(x * cellSize + cellSize / 2, y * cellSize + cellSize / 2);
                    if (center.Distance(point) <= radius)
                    {
                        map[x, y] = 0;
                    }
                }
            }
        }

        
        public class PointComparer : IEqualityComparer<Point>
        {
            public bool Equals(Point a, Point b) =>
                Math.Abs(a.X - b.X) < 0.0001 &&
                Math.Abs(a.Y - b.Y) < 0.0001;

            public int GetHashCode(Point p) =>
                (Math.Round(p.X, 4).GetHashCode() * 397) ^
                Math.Round(p.Y, 4).GetHashCode();
        }
    }
}