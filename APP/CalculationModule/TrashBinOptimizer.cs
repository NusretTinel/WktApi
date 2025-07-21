using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.IO;
using NetTopologySuite.Geometries;
using SimplePointApplication.Entity;
using SimplePointApplication.Tools;

namespace SimplePointApplication.Optimizers
{
    public class TrashBinOptimizer
    {
        private static readonly WKTReader _wktReader = new WKTReader();
        private readonly List<WktModel> _trashBins;
        private readonly Optimizer _optimizer;
        private const int TargetSRID = 54009; // GHS_POP's Mollweide projection

        public class WKTProcessor
        {
            public static List<Point> ParseWKTToPoints(List<WktModel> models)
            {
                var points = new List<Point>();
                foreach (var model in models)
                {
                    if (string.IsNullOrWhiteSpace(model.Wkt))
                        continue;

                    try
                    {
                        var geometry = _wktReader.Read(model.Wkt);
                        if (geometry is Point point)
                        {
                            var resultPoint = new Point(point.X, point.Y);
                            if (point.SRID != TargetSRID)
                            {
                                resultPoint = CoordinateConverter.ConvertPoint(point, TargetSRID);
                            }
                            points.Add(resultPoint);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                return points;
            }

            public static List<WktModel> ConvertPointsToWKTModels(List<Point> points)
            {
                return points.Select(p =>
                {
                    if (p.SRID != 4326) // Always output in WGS84 (EPSG:4326)
                    {
                        p = CoordinateConverter.ConvertPoint(p, 4326);
                    }
                    return new WktModel
                    {
                        Geometry = p,
                        Wkt = p.ToText(),
                        Name = "Optimized Bin"
                    };
                }).ToList();
            }
        }

        public TrashBinOptimizer(List<WktModel> trashBins)
        {
            _trashBins = trashBins;
            _optimizer = new Optimizer();
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
            // Convert bins to points in target CRS
            var existingBins = WKTProcessor.ParseWKTToPoints(_trashBins);

            // Get optimized bins
            var optimizedBins = _optimizer.Optimize(
                populationDataSourcePath,
                existingBins,
                gridWidth,
                gridHeight,
                cellSize,
                newBinCount,
                minDistance,
                polygonWkt);

            // Convert back to WGS84 (EPSG:4326)
            return WKTProcessor.ConvertPointsToWKTModels(optimizedBins);
        }
    }

    public static class CoordinateConverter
    {
        public static Point ConvertPoint(Point point, int targetSRID)
        {
            // Implementation using ProjNet or other coordinate transformation library
            // This is a simplified placeholder - actual implementation would use proper transformation
            return new Point(point.X, point.Y) { SRID = targetSRID };
        }
    }
}