using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OpenCvSharp;
using Point = NetTopologySuite.Geometries.Point;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        private class PopulationDataSource : IDisposable
        {
            private readonly Bitmap _image;
            private readonly AffineTransform _transform;
            private readonly int _noDataValue = -200;

            public PopulationDataSource(string path)
            {
                try
                {
                    // Load with explicit validation
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    _image = (Bitmap)Image.FromStream(fs);

                    // Default transform (override with your actual georeferencing)
                    _transform = new AffineTransform(new double[] { 0, 1, 0, 0, 0, 1 });
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load population map: {ex.Message}");
                }
            }

            public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
            {
                if (bounds == null || bounds.IsEmpty)
                    throw new ArgumentException("Invalid polygon bounds");

                var heatmap = new double[width, height];
                var env = bounds.EnvelopeInternal;

                try
                {
                    var bitmapData = _image.LockBits(
                        new Rectangle(0, 0, _image.Width, _image.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

                    unsafe
                    {
                        byte* ptr = (byte*)bitmapData.Scan0;
                        int stride = bitmapData.Stride;

                        for (int x = 0; x < width; x++)
                        {
                            for (int y = 0; y < height; y++)
                            {
                                double geoX = env.MinX + (env.MaxX - env.MinX) * x / width;
                                double geoY = env.MinY + (env.MaxY - env.MinY) * y / height;

                                var point = new Point(geoX, geoY);
                                if (bounds.Contains(point))
                                {
                                    _transform.GeoToPixel(geoX, geoY, out int pixelX, out int pixelY);

                                    if (pixelX >= 0 && pixelX < _image.Width &&
                                        pixelY >= 0 && pixelY < _image.Height)
                                    {
                                        byte* row = ptr + (pixelY * stride);
                                        int r = row[pixelX * 4 + 2]; // Red channel
                                        heatmap[x, y] = r == 0 ? 0 : r; // Treat 0 as NODATA
                                    }
                                }
                            }
                        }
                    }
                    _image.UnlockBits(bitmapData);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to process population data: {ex.Message}");
                }

                return heatmap;
            }

            public void Dispose()
            {
                _image?.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private class AffineTransform
        {
            private readonly double[] _transform;

            public AffineTransform(double[] transform) => _transform = transform;

            public void GeoToPixel(double geoX, double geoY, out int pixelX, out int pixelY)
            {
                pixelX = (int)((geoX - _transform[0]) / _transform[1]);
                pixelY = (int)((geoY - _transform[3]) / _transform[5]);
            }
        }

        private double[,] CalculateDifferenceMap(double[,] population, List<Point> existingBins, double cellSize)
        {
            int width = population.GetLength(0);
            int height = population.GetLength(1);
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
                    difference[x, y] = population[x, y] - trashbinHeatmap[x, y];

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

                Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(kernelSize, kernelSize), sigma);

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

        public List<Point> Optimize(
            string populationDataSourcePath,
            List<Point> existingBins,
            int gridWidth,
            int gridHeight,
            double cellSize,
            int binCount,
            double minDistance,
            string polygonWkt = null)
        {
            using (var dataSource = new PopulationDataSource(populationDataSourcePath))
            {
                Polygon polygon = null;
                if (!string.IsNullOrEmpty(polygonWkt))
                {
                    polygon = (Polygon)new WKTReader().Read(polygonWkt);
                }

                var populationHeatmap = dataSource.GetPopulationDataForArea(gridWidth, gridHeight, polygon ?? CreateDefaultPolygon(gridWidth, gridHeight, cellSize));
                var differenceMap = CalculateDifferenceMap(populationHeatmap, existingBins, cellSize);
                var optimizedBins = FindPeaks(differenceMap, cellSize, binCount, minDistance, polygon);

                return optimizedBins;
            }
        }

        private Polygon CreateDefaultPolygon(int width, int height, double cellSize)
        {
            var coordinates = new Coordinate[]
            {
                new Coordinate(0, 0),
                new Coordinate(width * cellSize, 0),
                new Coordinate(width * cellSize, height * cellSize),
                new Coordinate(0, height * cellSize),
                new Coordinate(0, 0)
            };
            return new Polygon(new LinearRing(coordinates));
        }

        private class PointComparer : IEqualityComparer<Point>
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