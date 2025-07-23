using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SimplePointApplication.Entity;
using SimplePointApplication.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SimplePointApplication.Optimizers
{
    public class ParkingOptimizer : IDisposable
    {
        private readonly Optimizer _optimizer;
        private readonly string _populationDataPath;
        private readonly ILogger _logger;
        private const int TargetSRID = 54009;
        private const int OutputSRID = 4326;
        private static readonly WKTReader _wktReader = new WKTReader();

        public ParkingOptimizer(string populationDataPath, ILogger logger = null)
        {
            _populationDataPath = populationDataPath;
            _logger = logger;
            _optimizer = new Optimizer();
            _logger?.LogInformation("Parking optimizer initialized");
        }

        public List<WktModel> OptimizeParkingSpots(
    List<WktModel> candidateSpots,
    int topN,
    double minDistance,
    double cellSize)
        {
            try
            {
                _logger?.LogInformation("Starting optimization with {Count} candidate spots", candidateSpots.Count);

                // 1. Parse and convert input points
                var candidatePoints = ParseAndConvertPoints(candidateSpots);
                if (!candidatePoints.Any())
                {
                    throw new Exception("No valid points could be parsed from input");
                }

                // 2. Calculate envelope in target SRID
                var envelope = CalculateEnvelope(candidatePoints);
                _logger?.LogInformation($"Envelope in SRID {TargetSRID}: MinX={envelope.MinX}, MaxX={envelope.MaxX}, MinY={envelope.MinY}, MaxY={envelope.MaxY}");

                // 3. Calculate grid dimensions
                int gridWidth = Math.Max(1, (int)Math.Ceiling(envelope.Width / cellSize));
                int gridHeight = Math.Max(1, (int)Math.Ceiling(envelope.Height / cellSize));
                _logger?.LogInformation($"Using grid: {gridWidth}x{gridHeight}, Cell size: {cellSize}m");

                // 4. Get population data for the area
                using (var dataSource = new Optimizer.GdalPopulationDataSource(_populationDataPath))
                {
                    string envelopeText = $"POLYGON(({envelope.MinX} {envelope.MinY}, {envelope.MaxX} {envelope.MinY}, {envelope.MaxX} {envelope.MaxY}, {envelope.MinX} {envelope.MaxY}, {envelope.MinX} {envelope.MinY}))";
                    var population = dataSource.GetPopulationDataForArea(gridWidth, gridHeight,
                        new WKTReader().Read(envelopeText) as Polygon);

                    // 5. Calculate population reach for each candidate point
                    var scoredPoints = candidatePoints.Select(p =>
                    {
                        int x = (int)((p.X - envelope.MinX) / cellSize);
                        int y = (int)((p.Y - envelope.MinY) / cellSize);

                        if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                        {
                            return new { Point = p, Score = population[x, y] };
                        }
                        return new { Point = p, Score = 0.0 };
                    }).ToList();

                    // 6. Filter and sort by population reach
                    var topPoints = scoredPoints
                        .OrderByDescending(p => p.Score)
                        .Take(topN)
                        .Select(p => p.Point)
                        .ToList();

                    // 7. Convert results back to WGS84 and create WktModels
                    var results = ConvertResults(topPoints);
                    LogResults(results);

                    return results;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Optimization process failed");
                throw;
            }
        }

        private List<Point> ParseAndConvertPoints(List<WktModel> models)
        {
            var points = new List<Point>();
            foreach (var model in models)
            {
                try
                {
                    var geometry = _wktReader.Read(model.GetRawWkt()) as Point;
                    if (geometry == null)
                    {
                        _logger?.LogWarning("Invalid point geometry in model ID: {Id}", model.Id);
                        continue;
                    }

                    // Set default SRID if not specified
                    geometry.SRID = geometry.SRID == -1 ? 4326 : geometry.SRID;

                    // Convert to target SRID for calculations
                    var convertedPoint = geometry.SRID == TargetSRID
                        ? geometry
                        : CoordinateConverter.ConvertPoint(geometry, TargetSRID);

                    points.Add(convertedPoint);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to parse point from model {model.Id}");
                }
            }
            return points;
        }

        private List<WktModel> ConvertResults(List<Point> optimizedPoints)
        {
            var results = new List<WktModel>();
            foreach (var point in optimizedPoints)
            {
                try
                {
                    // Convert back to WGS84 for output
                    var outputPoint = point.SRID == OutputSRID
                        ? point
                        : CoordinateConverter.ConvertPoint(point, OutputSRID);

                    results.Add(new WktModel
                    {
                        Wkt = outputPoint.ToText(),
                        Geometry = outputPoint,
                        Name = "Optimized Parking Spot"
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to convert point {point} to output format");
                }
            }
            return results;
        }

        private void LogCoordinateConversion(List<WktModel> inputModels, List<Point> convertedPoints)
        {
            for (int i = 0; i < inputModels.Count; i++)
            {
                if (i < convertedPoints.Count)
                {
                    _logger?.LogDebug($"Point {i}: Original: {inputModels[i].Wkt} -> Converted: SRID={convertedPoints[i].SRID}, X={convertedPoints[i].X}, Y={convertedPoints[i].Y}");
                }
            }
        }

        private void LogResults(List<WktModel> results)
        {
            foreach (var result in results)
            {
                _logger?.LogDebug($"Optimized point: {result.Wkt}");
            }
        }

        private Envelope CalculateEnvelope(List<Point> points)
        {
            var envelope = new Envelope();
            foreach (var point in points)
            {
                envelope.ExpandToInclude(point.Coordinate);
            }

            // Add 10% buffer to ensure all points are within bounds
            envelope.ExpandBy(envelope.Width * 0.1, envelope.Height * 0.1);

            return envelope;
        }

        public void Dispose()
        {
            // Clean up if needed
        }
    }
}