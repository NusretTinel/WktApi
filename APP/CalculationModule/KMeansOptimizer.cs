using Accord.MachineLearning;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using OSGeo.OGR;
using SimplePointApplication.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using Envelope= NetTopologySuite.Geometries.Envelope;
using static SimplePointApplication.Optimizers.TrashBinOptimizer;

namespace SimplePointApplication.Optimizers
{
    public class KMeansOptimizer
    {
        private readonly List<Point> _existingBins;
        private const int TargetSRID = 54009;

        public KMeansOptimizer(List<WktModel> existingBins)
        {
            _existingBins = WKTProcessor.ParseWKTToPoints(existingBins);
        }

        public List<WktModel> OptimizeTrashBins(
            string populationDataSourcePath,
            int gridWidth,
            int gridHeight,
            double cellSize,
            int newBinCount,
            double minDistance,
            string polygonWkt = null)
        {
            // Step 1: Get population data
            double[,] population = GetPopulationData(
                populationDataSourcePath,
                gridWidth,
                gridHeight,
                polygonWkt);

            // Step 2: Convert to weighted points
            var weightedPoints = ConvertToWeightedPoints(population, cellSize);
            if (weightedPoints.Count == 0)
                return new List<WktModel>();

            // Step 3: Run K-means
            var centroids = RunKMeans(weightedPoints, newBinCount, minDistance);

            // Step 4: Convert back to WKT
            return WKTProcessor.ConvertPointsToWKTModels(centroids);
        }

        private double[,] GetPopulationData(
            string path,
            int width,
            int height,
            string polygonWkt)
        {
            using (var dataSource = new GdalPopulationDataSource(path))
            {
                var polygon = string.IsNullOrEmpty(polygonWkt)
                    ? null
                    : (Polygon)new WKTReader().Read(polygonWkt);

                return dataSource.GetPopulationDataForArea(width, height, polygon);
            }
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
    }

    internal class GdalPopulationDataSource : IPopulationDataSource
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

    internal interface IPopulationDataSource : IDisposable
    {
        double[,] GetPopulationDataForArea(int width, int height, Polygon bounds);
    }
}