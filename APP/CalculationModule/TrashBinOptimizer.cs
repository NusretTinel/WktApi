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
using System.Runtime.InteropServices;
namespace SimplePointApplication.Optimizers
{
    public class TrashBinOptimizer
    {
        private static readonly WKTReader _wktReader = new WKTReader();
        private readonly List<WktModel> _trashBins;
        private readonly Optimizer _optimizer;
        private const int TargetSRID = 54009; 

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
                    Console.WriteLine($"Before conversion - X: {p.X}, Y: {p.Y}, SRID: {p.SRID}");

                    if (p.SRID != 4326) 
                    {
                        p = CoordinateConverter.ConvertPoint(p, 4326);
                        Console.WriteLine($"After conversion - X: {p.X}, Y: {p.Y}, SRID: {p.SRID}");
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
            
            var existingBins = WKTProcessor.ParseWKTToPoints(_trashBins);

            var optimizedBins = _optimizer.Optimize(
                populationDataSourcePath,
                existingBins,
                gridWidth,
                gridHeight,
                cellSize,
                newBinCount,
                minDistance,
                polygonWkt);

            
            return WKTProcessor.ConvertPointsToWKTModels(optimizedBins);
        }
    }


        public static class CoordinateConverter
        {
            private const string MollweideEsriWkt = @"
        PROJCS[""World_Mollweide"",
            GEOGCS[""WGS_84"",
                DATUM[""WGS_1984"",
                    SPHEROID[""WGS84"",6378137,298.257223563]],
                PRIMEM[""Greenwich"",0],
                UNIT[""Degree"",0.0174532925199433]],
            PROJECTION[""Mollweide""],
            PARAMETER[""False_Easting"",0],
            PARAMETER[""False_Northing"",0],
            PARAMETER[""Central_Meridian"",0],
            UNIT[""Meter"",1],
            AUTHORITY[""ESRI"",""54009""]]";

            static CoordinateConverter()
            {
                GdalConfiguration.ConfigureGdal();
                Gdal.AllRegister();
                Ogr.RegisterAll();
            }

        public static Point ConvertPoint(Point point, int targetSRID)
        {
            try
            {
                int sourceSRID = point.SRID == 0 ? 4326 : point.SRID;
                Console.WriteLine($"Converting from SRID {sourceSRID} to {targetSRID}");

                using (var sourceSR = new SpatialReference(""))
                using (var targetSR = new SpatialReference(""))
                {
                    
                    if (sourceSRID == 54009)
                    {
                        string wkt = MollweideEsriWkt;
                        if (sourceSR.ImportFromWkt(ref wkt) != 0)
                            throw new Exception("Failed to create source spatial reference");
                    }
                    else
                    {
                        if (sourceSR.ImportFromEPSG(sourceSRID) != 0)
                            throw new Exception("Failed to create source spatial reference");
                    }

                  
                    if (targetSRID == 54009)
                    {
                        string wkt = MollweideEsriWkt;
                        if (targetSR.ImportFromWkt(ref wkt) != 0)
                            throw new Exception("Failed to create target spatial reference");
                    }
                    else
                    {
                        if (targetSR.ImportFromEPSG(targetSRID) != 0)
                            throw new Exception("Failed to create target spatial reference");
                    }

                    using (var transform = new CoordinateTransformation(sourceSR, targetSR))
                    {
                        if (transform == null)
                            throw new Exception("Failed to create coordinate transformation");

                        double[] x = { point.X };
                        double[] y = { point.Y };
                        double[] z = { 0 };

                        transform.TransformPoints(1, x, y, z);
                        return new Point(x[0], y[0]) { SRID = targetSRID };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Coordinate conversion failed: {ex.Message}");
                throw;
            }
        }
    }
    }


