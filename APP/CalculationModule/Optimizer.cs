using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OpenCvSharp;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using Point = NetTopologySuite.Geometries.Point;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        private interface IPopulationDataSource : IDisposable
        {
            double[,] GetPopulationDataForArea(int width, int height, Polygon bounds);
        }

        // Primary: GDAL for GeoTIFFs

            private class GdalPopulationDataSource : IPopulationDataSource
            {
                private readonly Dataset _dataset;
                private readonly double[] _geoTransform = new double[6];

                public GdalPopulationDataSource(string path)
                {
                    try
                    {
                        _dataset = Gdal.Open(path, Access.GA_ReadOnly) ??
                            throw new Exception("GDAL returned null dataset");
                        _dataset.GetGeoTransform(_geoTransform);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"GDAL initialization failed: {ex.Message}");
                    }
                }

                public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
                {
                    var heatmap = new double[width, height];
                    var env = bounds.EnvelopeInternal;

                    Band band = _dataset.GetRasterBand(1);
                    double noDataValue = 0;
                    int hasVal;
                    band.GetNoDataValue(out noDataValue, out hasVal);
                    if (hasVal == 0) noDataValue = 0;

                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            double geoX = env.MinX + (env.MaxX - env.MinX) * x / width;
                            double geoY = env.MinY + (env.MaxY - env.MinY) * y / height;

                            if (bounds.Contains(new Point(geoX, geoY)))
                            {
                                GeoToPixel(geoX, geoY, out int pixelX, out int pixelY);
                                if (IsInImage(pixelX, pixelY))
                                {
                                    double[] value = new double[1];
                                    band.ReadRaster(pixelX, pixelY, 1, 1, value, 1, 1, 0, 0);
                                    heatmap[x, y] = (hasVal != 0 && value[0] == noDataValue) ? 0 : value[0];
                                }
                            }
                        }
                    }
                    return heatmap;
                }

                private void GeoToPixel(double geoX, double geoY, out int pixelX, out int pixelY)
                {
                    pixelX = (int)((geoX - _geoTransform[0]) / _geoTransform[1]);
                    pixelY = (int)((geoY - _geoTransform[3]) / _geoTransform[5]);
                }

                private bool IsInImage(int x, int y) =>
                    x >= 0 && x < _dataset.RasterXSize &&
                    y >= 0 && y < _dataset.RasterYSize;

                public void Dispose() => _dataset?.Dispose();
            }

            // Fallback: System.Drawing for standard TIFFs
            private class BitmapPopulationDataSource : IPopulationDataSource
        {
            private readonly Bitmap _image;
            private readonly Envelope _bounds;

            public BitmapPopulationDataSource(string path)
            {
                try
                {
                    _image = new Bitmap(path);
                    // Turkey approximate bounding box (WGS84)
                    _bounds = new Envelope(26.0, 45.0, 36.0, 42.0);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Bitmap loading failed: {ex.Message}");
                }
            }

            public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
            {
                var heatmap = new double[width, height];
                var env = bounds?.EnvelopeInternal ?? _bounds;

                double xScale = _image.Width / _bounds.Width;
                double yScale = _image.Height / _bounds.Height;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        double geoX = env.MinX + env.Width * x / width;
                        double geoY = env.MaxY - env.Height * y / height; // Flip Y

                        int imgX = (int)((geoX - _bounds.MinX) * xScale);
                        int imgY = (int)((_bounds.MaxY - geoY) * yScale);

                        if (imgX >= 0 && imgX < _image.Width && imgY >= 0 && imgY < _image.Height)
                        {
                            Color pixel = _image.GetPixel(imgX, imgY);
                            heatmap[x, y] = pixel.R; // Using red channel
                        }
                    }
                }
                return heatmap;
            }

            public void Dispose() => _image?.Dispose();
        }

        // Core optimization logic
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
            using (var dataSource = CreateBestDataSource(populationDataSourcePath))
            {
                Polygon polygon = string.IsNullOrEmpty(polygonWkt) ?
                    CreateDefaultPolygon(gridWidth, gridHeight, cellSize) :
                    (Polygon)new WKTReader().Read(polygonWkt);

                var population = dataSource.GetPopulationDataForArea(gridWidth, gridHeight, polygon);
                var difference = CalculateDifferenceMap(population, existingBins, cellSize);
                return FindPeaks(difference, cellSize, binCount, minDistance, polygon);
            }
        }

        private IPopulationDataSource CreateBestDataSource(string path)
        {
            // Try GDAL first
            try
            {
                return new GdalPopulationDataSource(path);
            }
            catch (Exception gdalEx)
            {
                Console.WriteLine($"GDAL failed, trying Bitmap: {gdalEx.Message}");
                try
                {
                    return new BitmapPopulationDataSource(path);
                }
                catch (Exception bitmapEx)
                {
                    throw new AggregateException(
                        "All data source options failed",
                        new[] { gdalEx, bitmapEx });
                }
            }
        }

        private double[,] CalculateDifferenceMap(double[,] population, List<Point> existingBins, double cellSize)
        {
            int width = population.GetLength(0);
            int height = population.GetLength(1);
            var binHeatmap = new double[width, height];

            // Mark existing bins
            foreach (var bin in existingBins)
            {
                int x = (int)(bin.X / cellSize);
                int y = (int)(bin.Y / cellSize);
                if (x >= 0 && x < width && y >= 0 && y < height)
                    binHeatmap[x, y] += 1;
            }

            // Smooth bin distribution
            binHeatmap = ApplyGaussianBlur(binHeatmap, 7, 1.5);

            // Calculate difference
            var difference = new double[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    difference[x, y] = population[x, y] - binHeatmap[x, y];

            return difference;
        }

        private List<Point> FindPeaks(double[,] map, double cellSize, int count, double minDistance, Polygon bounds)
        {
            var peaks = new List<Point>();
            int minDistCells = (int)(minDistance / cellSize);

            for (int i = 0; i < count; i++)
            {
                (int x, int y) = FindMaxValue(map, bounds, cellSize);
                if (map[x, y] <= 0) break;

                peaks.Add(new Point(
                    x * cellSize + cellSize / 2,
                    y * cellSize + cellSize / 2));

                // Clear surrounding area
                for (int dx = -minDistCells; dx <= minDistCells; dx++)
                    for (int dy = -minDistCells; dy <= minDistCells; dy++)
                        if (x + dx >= 0 && x + dx < map.GetLength(0) &&
                            y + dy >= 0 && y + dy < map.GetLength(1))
                            map[x + dx, y + dy] = 0;
            }
            return peaks;
        }

        private (int x, int y) FindMaxValue(double[,] map, Polygon bounds, double cellSize)
        {
            int maxX = 0, maxY = 0;
            double maxVal = 0;

            for (int x = 0; x < map.GetLength(0); x++)
            {
                for (int y = 0; y < map.GetLength(1); y++)
                {
                    if (map[x, y] > maxVal)
                    {
                        var point = new Point(
                            x * cellSize + cellSize / 2,
                            y * cellSize + cellSize / 2);

                        if (bounds == null || bounds.Contains(point))
                        {
                            maxVal = map[x, y];
                            maxX = x;
                            maxY = y;
                        }
                    }
                }
            }
            return (maxX, maxY);
        }

        private double[,] ApplyGaussianBlur(double[,] input, int kernelSize, double sigma)
        {
            using (var src = new Mat(input.GetLength(1), input.GetLength(0), MatType.CV_64FC1))
            using (var dst = new Mat())
            {
                // Copy data to OpenCV matrix
                for (int y = 0; y < src.Rows; y++)
                    for (int x = 0; x < src.Cols; x++)
                        src.Set(y, x, input[x, y]);

                Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(kernelSize, kernelSize), sigma);

                // Copy back
                var output = new double[input.GetLength(0), input.GetLength(1)];
                for (int y = 0; y < dst.Rows; y++)
                    for (int x = 0; x < dst.Cols; x++)
                        output[x, y] = dst.Get<double>(y, x);

                return output;
            }
        }

        private Polygon CreateDefaultPolygon(int width, int height, double cellSize)
        {
            return new Polygon(new LinearRing(new[]
            {
                new Coordinate(0, 0),
                new Coordinate(width * cellSize, 0),
                new Coordinate(width * cellSize, height * cellSize),
                new Coordinate(0, height * cellSize),
                new Coordinate(0, 0)
            }));
        }
    }
}