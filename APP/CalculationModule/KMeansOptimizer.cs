using Accord.MachineLearning;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Linq;
using Envelope = NetTopologySuite.Geometries.Envelope;

namespace SimplePointApplication.Optimizers
{
    public class KMeansOptimizer
    {
        private readonly List<Point> _existingBins;
        private const int TargetSRID = 54009;

        public KMeansOptimizer(List<Point> existingBins)
        {
            _existingBins = existingBins ?? new List<Point>();
        }

        public List<Point> Optimize(
            string populationDataSourcePath,
            int gridWidth,
            int gridHeight,
            double cellSize,
            int binCount,
            double minDistance,
            string polygonWkt = null)
        {
            using (var dataSource = CreateBestDataSource(populationDataSourcePath))
            {
                Polygon polygon = string.IsNullOrEmpty(polygonWkt) 
                    ? CreateDefaultPolygon(gridWidth, gridHeight, cellSize)
                    : (Polygon)new WKTReader().Read(polygonWkt);

                // Step 1: Get population data
                var population = dataSource.GetPopulationDataForArea(gridWidth, gridHeight, polygon);

                // Step 2: Calculate difference map considering existing bins
                var difference = CalculateDifferenceMap(population, _existingBins, cellSize);

                // Step 3: Convert to weighted points
                var weightedPoints = ConvertToWeightedPoints(difference, cellSize);
                if (weightedPoints.Count == 0)
                    return new List<Point>();

                // Step 4: Run K-means
                var centroids = RunKMeans(weightedPoints, binCount, minDistance);

                return centroids;
            }
        }

        private IPopulationDataSource CreateBestDataSource(string path)
        {
            try
            {
                return new GdalPopulationDataSource(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create GDAL data source: {ex.Message}");
            }
        }

        private double[,] CalculateDifferenceMap(double[,] population, List<Point> existingBins, double cellSize)
        {
            int width = population.GetLength(0);
            int height = population.GetLength(1);
            var binHeatmap = new double[width, height];

            // Mark existing bin locations
            foreach (var bin in existingBins)
            {
                int x = (int)(bin.X / cellSize);
                int y = (int)(bin.Y / cellSize);
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    binHeatmap[x, y] += 10;
                }
            }

            // Apply Gaussian blur to existing bins
            binHeatmap = ApplyGaussianBlur(binHeatmap, 15, 3.0);

            // Calculate difference between population and existing bins
            var difference = new double[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    difference[x, y] = Math.Max(0, population[x, y] - binHeatmap[x, y] * 0.5);
                }
            }

            return difference;
        }

        private List<(double X, double Y, double Weight)> ConvertToWeightedPoints(
            double[,] population, double cellSize)
        {
            var points = new List<(double, double, double)>();
            for (int x = 0; x < population.GetLength(0); x++)
            {
                for (int y = 0; y < population.GetLength(1); y++)
                {
                    if (population[x, y] > 0)
                    {
                        points.Add((
                            x * cellSize + cellSize / 2,
                            y * cellSize + cellSize / 2,
                            population[x, y]));
                    }
                }
            }
            return points;
        }

        private List<Point> RunKMeans(
            List<(double X, double Y, double Weight)> points,
            int binCount,
            double minDistance)
        {
            double[][] observations = points.Select(p => new[] { p.X, p.Y }).ToArray();
            double[] weights = points.Select(p => p.Weight).ToArray();

            var kmeans = new KMeans(binCount)
            {
                Tolerance = 0.05,
                MaxIterations = 100
            };

            var clusters = kmeans.Learn(observations, weights);
            var centroids = clusters.Centroids
                .Select(c => new Point(c[0], c[1]) { SRID = TargetSRID })
                .ToList();

            return AdjustForMinDistance(centroids, minDistance);
        }

        private List<Point> AdjustForMinDistance(List<Point> points, double minDistance)
        {
            bool adjusted;
            do
            {
                adjusted = false;
                for (int i = 0; i < points.Count; i++)
                {
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        double distance = points[i].Distance(points[j]);
                        if (distance < minDistance)
                        {
                            double dx = points[j].X - points[i].X;
                            double dy = points[j].Y - points[i].Y;
                            double norm = Math.Sqrt(dx * dx + dy * dy);

                            if (norm > 0)
                            {
                                double push = (minDistance - distance) / 2;
                                points[i] = new Point(
                                    points[i].X - dx / norm * push,
                                    points[i].Y - dy / norm * push)
                                { SRID = TargetSRID };

                                points[j] = new Point(
                                    points[j].X + dx / norm * push,
                                    points[j].Y + dy / norm * push)
                                { SRID = TargetSRID };

                                adjusted = true;
                            }
                        }
                    }
                }
            } while (adjusted);

            return points;
        }

        private double[,] ApplyGaussianBlur(double[,] input, int kernelSize, double sigma)
        {
            using (var src = new OpenCvSharp.Mat(input.GetLength(1), input.GetLength(0), OpenCvSharp.MatType.CV_64FC1))
            using (var dst = new OpenCvSharp.Mat())
            {
                // Transpose the input to match OpenCV's row/column order
                for (int y = 0; y < src.Rows; y++)
                    for (int x = 0; x < src.Cols; x++)
                        src.Set(y, x, input[x, y]);

                OpenCvSharp.Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(kernelSize, kernelSize), sigma);

                // Transpose back to original order
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

        private class GdalPopulationDataSource : IPopulationDataSource
        {
            private readonly Dataset _dataset;
            private readonly double[] _geoTransform = new double[6];

            public GdalPopulationDataSource(string path)
            {
                _dataset = Gdal.Open(path, Access.GA_ReadOnly);
                if (_dataset == null)
                    throw new Exception("Failed to open GDAL dataset");
                _dataset.GetGeoTransform(_geoTransform);
            }

            public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
            {
                var heatmap = new double[width, height];
                var env = bounds?.EnvelopeInternal ?? GetDefaultEnvelope();

                Band band = _dataset.GetRasterBand(1);
                double noDataValue = 0;
                int hasVal;
                band.GetNoDataValue(out noDataValue, out hasVal);

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        double geoX = env.MinX + (env.MaxX - env.MinX) * x / width;
                        double geoY = env.MinY + (env.MaxY - env.MinY) * y / height;

                        if (bounds == null || bounds.Contains(new Point(geoX, geoY)))
                        {
                            GeoToPixel(geoX, geoY, out int px, out int py);
                            if (IsInImage(px, py))
                            {
                                double[] value = new double[1];
                                band.ReadRaster(px, py, 1, 1, value, 1, 1, 0, 0);
                                heatmap[x, y] = (hasVal != 0 && value[0] == noDataValue) ? 0 : value[0];
                            }
                        }
                    }
                }
                return heatmap;
            }

            public void Dispose()
            {
                _dataset?.Dispose();
                GC.SuppressFinalize(this);
            }

            private Envelope GetDefaultEnvelope()
            {
                double minX = _geoTransform[0];
                double maxY = _geoTransform[3];
                double maxX = minX + _geoTransform[1] * _dataset.RasterXSize;
                double minY = maxY + _geoTransform[5] * _dataset.RasterYSize;
                return new Envelope(minX, maxX, minY, maxY);
            }

            private void GeoToPixel(double geoX, double geoY, out int pixelX, out int pixelY)
            {
                pixelX = (int)((geoX - _geoTransform[0]) / _geoTransform[1]);
                pixelY = (int)((geoY - _geoTransform[3]) / _geoTransform[5]);
            }

            private bool IsInImage(int x, int y) =>
                x >= 0 && x < _dataset.RasterXSize &&
                y >= 0 && y < _dataset.RasterYSize;
        }

        private interface IPopulationDataSource : IDisposable
        {
            double[,] GetPopulationDataForArea(int width, int height, Polygon bounds);
        }
    }
}