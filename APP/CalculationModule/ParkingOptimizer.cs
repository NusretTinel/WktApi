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

        // Configuration constants
        private const int MaxRecommendedGridDimension = 10000; // 10,000 × 10,000 grid
        private const double MinRecommendedCellSize = 0.0001; // ~10 meters in degrees
        private const int MaxTotalCells = 100000000; // 100M elements (10,000 × 10,000)

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
            double requestedCellSize)
        {
            try
            {
                _logger?.LogInformation("Beginning parking spot optimization");

                var candidatePoints = ParseWKTToPoints(candidateSpots);
                if (!candidatePoints.Any())
                {
                    _logger?.LogError("No valid points could be parsed from input");
                    throw new Exception("No valid geographic points found in input");
                }

                var envelope = CalculateEnvelope(candidatePoints);
                var (gridWidth, gridHeight, effectiveCellSize) =
                    CalculateSafeGridDimensions(envelope, requestedCellSize);

                _logger?.LogDebug($"Using grid: {gridWidth}x{gridHeight}, Cell size: {effectiveCellSize}");

                var optimizedPoints = _optimizer.Optimize(
                    _populationDataPath,
                    candidatePoints,
                    gridWidth,
                    gridHeight,
                    effectiveCellSize,
                    topN,
                    minDistance,
                    null);

                return ConvertToWktModels(optimizedPoints);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Optimization process failed");
                throw;
            }
        }

        private (int width, int height, double cellSize) CalculateSafeGridDimensions(
            Envelope envelope,
            double requestedCellSize)
        {
            // Calculate initial dimensions
            double rawWidth = envelope.Width / requestedCellSize;
            double rawHeight = envelope.Height / requestedCellSize;

            // Check if we need adjustment
            bool needsAdjustment =
                rawWidth > MaxRecommendedGridDimension ||
                rawHeight > MaxRecommendedGridDimension ||
                requestedCellSize < MinRecommendedCellSize ||
                (rawWidth * rawHeight) > MaxTotalCells;

            if (!needsAdjustment)
            {
                return (
                    (int)Math.Ceiling(rawWidth),
                    (int)Math.Ceiling(rawHeight),
                    requestedCellSize
                );
            }

            // Calculate required adjustment factor
            double widthRatio = rawWidth / MaxRecommendedGridDimension;
            double heightRatio = rawHeight / MaxRecommendedGridDimension;
            double cellSizeRatio = MinRecommendedCellSize / requestedCellSize;
            double areaRatio = (rawWidth * rawHeight) / MaxTotalCells;

            double adjustmentFactor = Math.Max(
                Math.Max(widthRatio, heightRatio),
                Math.Max(cellSizeRatio, Math.Sqrt(areaRatio)));

            double adjustedCellSize = requestedCellSize * adjustmentFactor;

            // Calculate final dimensions
            int finalWidth = (int)Math.Ceiling(envelope.Width / adjustedCellSize);
            int finalHeight = (int)Math.Ceiling(envelope.Height / adjustedCellSize);

            _logger?.LogWarning($"Adjusted grid from {rawWidth}x{rawHeight} to {finalWidth}x{finalHeight} " +
                              $"with cell size {adjustedCellSize} (requested: {requestedCellSize})");

            return (finalWidth, finalHeight, adjustedCellSize);
        }

        private List<Point> ParseWKTToPoints(List<WktModel> models)
        {
            var points = new List<Point>();

            foreach (var model in models)
            {
                try
                {
                    var wkt = model.GetRawWkt();
                    if (string.IsNullOrWhiteSpace(wkt))
                    {
                        _logger?.LogWarning("Empty WKT string in model ID: {Id}", model.Id);
                        continue;
                    }

                    var geometry = _wktReader.Read(wkt) as Point;
                    if (geometry == null)
                    {
                        _logger?.LogWarning("Invalid point geometry in model ID: {Id}", model.Id);
                        continue;
                    }

                    if (double.IsNaN(geometry.X) || double.IsNaN(geometry.Y))
                    {
                        _logger?.LogWarning("Invalid coordinates in model ID: {Id}", model.Id);
                        continue;
                    }

                    // Assign default SRID if not set (WGS84)
                    if (geometry.SRID == -1)
                    {
                        geometry.SRID = 4326;
                        _logger?.LogDebug($"Assigned default SRID 4326 to point from model ID: {model.Id}");
                    }

                    var point = geometry.SRID == TargetSRID ?
                        geometry :
                        CoordinateConverter.ConvertPoint(geometry, TargetSRID);

                    points.Add(point);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to parse WKT in model ID: {model.Id}");
                }
            }

            _logger?.LogInformation($"Parsed {points.Count} valid points from {models.Count} inputs");
            return points;
        }

        private List<WktModel> ConvertToWktModels(List<Point> points)
        {
            return points.Select(p =>
            {
                try
                {
                    var outputPoint = p.SRID == OutputSRID ?
                        p :
                        CoordinateConverter.ConvertPoint(p, OutputSRID);

                    return new WktModel
                    {
                        Wkt = outputPoint.ToText(),
                        Geometry = outputPoint,
                        Name = "Optimized Parking Spot"
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to convert point: {p}");
                    return null;
                }
            }).Where(x => x != null).ToList();
        }

        private Envelope CalculateEnvelope(List<Point> points)
        {
            var envelope = new Envelope();
            foreach (var point in points)
            {
                envelope.ExpandToInclude(point.Coordinate);
            }
            return envelope;
        }

        public void Dispose()
        {
            // Clean up resources if needed
        }
    }
}