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
        private readonly List<WktModel> _trashBins;
        private readonly Optimizer _optimizer;
        public class WKTProcessor
        {
            private static readonly WKTReader wktReader = new WKTReader();


            public static List<Point> ParseWKTToPoints(List<WktModel> models)
            {
                var points = new List<Point>();
                foreach (var model in models)
                {
                    if (string.IsNullOrWhiteSpace(model.Wkt))
                        continue; // Skip null/empty WKT

                    try
                    {
                        var geometry = wktReader.Read(model.Wkt);
                        if (geometry is Point point)
                            points.Add(new Point(point.X, point.Y) { SRID = point.SRID });
                    }
                    catch
                    {
                        continue; // Skip invalid WKT
                    }
                }
                return points;
            }

            public static List<WktModel> ConvertPointsToWKTModels(List<Point> points)
            {
                return points.Select(p => new WktModel
                {
                    Geometry = p, // Set Geometry directly (Wkt will be computed automatically)
                    Name = "Optimized Bin" // Optional: Set a default name
                }).ToList();
            }
        }

        public TrashBinOptimizer(List<WktModel> trashBins)
        {
            _trashBins = trashBins;
            _optimizer = new Optimizer();
        }

        public List<WktModel> OptimizeTrashBins(double[] [] populationHeatmap, double cellSize, int newBinCount, double minDistance)
        {
            var existingBins = WKTProcessor.ParseWKTToPoints(_trashBins);
            var optimizedBins = _optimizer.Optimize(populationHeatmap, existingBins, cellSize, newBinCount, minDistance);
            return WKTProcessor.ConvertPointsToWKTModels(optimizedBins);
        }
    }
}