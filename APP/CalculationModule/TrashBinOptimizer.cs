using APP;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using SimplePointApplication.Entity;
using SimplePointApplication.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
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
        static CoordinateConverter()
        {
            // Initialize GDAL once (critical!)
            GdalConfiguration.ConfigureGdal();
            Gdal.AllRegister();
            Ogr.RegisterAll();
        }

        public static Point ConvertPoint(Point point, int targetSRID)
        {
            try
            {
                // Handle null SRID (default to WGS84)
                int sourceSRID = point.SRID == 0 ? 4326 : point.SRID;

                // Setup spatial references
                using (var sourceSR = new SpatialReference(""))
                using (var targetSR = new SpatialReference(""))
                {
                    sourceSR.ImportFromEPSG(sourceSRID);
                    targetSR.ImportFromEPSG(targetSRID);

                    // Create and use transformation
                    using (var transform = new CoordinateTransformation(sourceSR, targetSR))
                    {
                        double[] x = { point.X };
                        double[] y = { point.Y };
                        double[] z = { 0 };

                        // Corrected TransformPoints call
                        transform.TransformPoints(1, x, y, z); // The first parameter is the point count

                        return new Point(x[0], y[0]) { SRID = targetSRID };
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback: return original point with warning
                Console.WriteLine($"Coordinate conversion failed: {ex.Message}");
                return new Point(point.X, point.Y) { SRID = targetSRID };
            }
        }
    }
           
 
}