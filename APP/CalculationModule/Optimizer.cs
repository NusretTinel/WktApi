using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using OpenCvSharp;
using Point = NetTopologySuite.Geometries.Point;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        private class PopulationDataSource : IDisposable
        {
            private Dataset _dataset;
            private readonly AffineTransform _transform;
            private readonly int _noDataValue = -200;
            private DataWindow window;

            public PopulationDataSource(string path)
            {
                Gdal.AllRegister();
                _dataset = Gdal.Open(path, Access.GA_ReadOnly); double[] transform = new double[6];
                _dataset.GetGeoTransform(transform);
                _transform = new AffineTransform(transform);
            }

            public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
            {
                window = CalculateDataWindow(bounds);
                float[] buffer = new float[window.Width * window.Height];
                _dataset.GetRasterBand(1).ReadRaster(
                    window.X, window.Y,
                    window.Width, window.Height,
                    buffer,
                    window.Width, window.Height,
                    0, 0);

                return ProcessWindowData(buffer, window.Width, window.Height, width, height, bounds);
            }

            private DataWindow CalculateDataWindow(Polygon bounds)
            {
                var env = bounds.EnvelopeInternal;
                _transform.GeoToPixel(env.MinX, env.MinY, out int x1, out int y1);
                _transform.GeoToPixel(env.MaxX, env.MaxY, out int x2, out int y2);

                return new DataWindow
                {
                    X = Math.Min(x1, x2),
                    Y = Math.Min(y1, y2),
                    Width = Math.Abs(x2 - x1) + 1,
                    Height = Math.Abs(y2 - y1) + 1
                };
            }

            private double[,] ProcessWindowData(float[] sourceData, int srcWidth, int srcHeight,
                                             int targetWidth, int targetHeight, Polygon bounds)
            {
                var heatmap = new double[targetWidth, targetHeight];
                double xRatio = (double)srcWidth / targetWidth;
                double yRatio = (double)srcHeight / targetHeight;

                for (int x = 0; x < targetWidth; x++)
                {
                    for (int y = 0; y < targetHeight; y++)
                    {
                        double geoX, geoY;
                        _transform.PixelToGeo(
                            x * xRatio + window.X,
                            y * yRatio + window.Y,
                            out geoX, out geoY);

                        if (bounds.Contains(new Point(geoX, geoY)))
                        {
                            double srcX = x * xRatio;
                            double srcY = y * yRatio;

                            int x0 = (int)Math.Floor(srcX);
                            int x1 = Math.Min(x0 + 1, srcWidth - 1);
                            int y0 = (int)Math.Floor(srcY);
                            int y1 = Math.Min(y0 + 1, srcHeight - 1);

                            double val = Interpolate(
                                sourceData[y0 * srcWidth + x0],
                                sourceData[y0 * srcWidth + x1],
                                sourceData[y1 * srcWidth + x0],
                                sourceData[y1 * srcWidth + x1],
                                srcX - x0, srcY - y0);

                            heatmap[x, y] = val > _noDataValue ? val : 0;
                        }
                    }
                }
                return heatmap;
            }

            private double Interpolate(double q11, double q21, double q12, double q22, double x, double y)
            {
                double r1 = q11 * (1 - x) + q21 * x;
                double r2 = q12 * (1 - x) + q22 * x;
                return r1 * (1 - y) + r2 * y;
            }

            public void Dispose()
            {
                _dataset?.Dispose();
            }
        }

        private class DataWindow
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private class AffineTransform
        {
            private readonly double[] _transform;

            public AffineTransform(double[] transform)
            {
                _transform = transform;
            }

            public void GeoToPixel(double geoX, double geoY, out int pixelX, out int pixelY)
            {
                pixelX = (int)((geoX - _transform[0]) / _transform[1]);
                pixelY = (int)((geoY - _transform[3]) / _transform[5]);
            }

            public void PixelToGeo(double pixelX, double pixelY, out double geoX, out double geoY)
            {
                geoX = _transform[0] + pixelX * _transform[1];
                geoY = _transform[3] + pixelY * _transform[5];
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