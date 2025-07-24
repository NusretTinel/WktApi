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
        
        private const int TargetSRID = 54009;
        private const int OutputSRID = 4326;
        private static readonly WKTReader _wktReader = new WKTReader();

        public ParkingOptimizer(string populationDataPath)
        {
            _populationDataPath = populationDataPath;
            
            _optimizer = new Optimizer();
           
        }

        public List<WktModel> OptimizeParkingSpots(
            List<WktModel> candidateSpots,
            int topN,
            double minDistance,
            double cellSize)
        {
            try
            {
                var candidatePoints = ParseAndConvertPoints(candidateSpots);
                if (!candidatePoints.Any())
                {
                    throw new Exception("No valid points could be parsed from input");
                }

                var envelope = CalculateEnvelope(candidatePoints);

                int gridWidth = Math.Max(1, (int)Math.Ceiling(envelope.Width / cellSize));
                int gridHeight = Math.Max(1, (int)Math.Ceiling(envelope.Height / cellSize));

                using (var dataSource = new Optimizer.GdalPopulationDataSource(_populationDataPath))
                {
                    string envelopeText = $"POLYGON(({envelope.MinX} {envelope.MinY}, {envelope.MaxX} {envelope.MinY}, {envelope.MaxX} {envelope.MaxY}, {envelope.MinX} {envelope.MaxY}, {envelope.MinX} {envelope.MinY}))";
                    var population = dataSource.GetPopulationDataForArea(gridWidth, gridHeight,
                        new WKTReader().Read(envelopeText) as Polygon);

                    var scoredItems = candidatePoints.Select((p, index) =>
                    {
                        int centerX = (int)((p.X - envelope.MinX) / cellSize);
                        int centerY = (int)((p.Y - envelope.MinY) / cellSize);

                        double score = 0;
                        int radius = (int)(minDistance / cellSize);

                        for (int x = Math.Max(0, centerX - radius); x <= Math.Min(gridWidth - 1, centerX + radius); x++)
                        {
                            for (int y = Math.Max(0, centerY - radius); y <= Math.Min(gridHeight - 1, centerY + radius); y++)
                            {
                                double distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2)) * cellSize;
                                if (distance <= minDistance)
                                {
                                    score += population[x, y] * (1 - distance / minDistance);
                                }
                            }
                        }

                        return new { Point = p, Score = score, OriginalModel = candidateSpots[index] };
                    }).ToList();

                    var topItems = new List<WktModel>();
                    var remainingItems = scoredItems.OrderByDescending(p => p.Score).ToList();

                    while (topItems.Count < topN && remainingItems.Any())
                    {
                        var bestItem = remainingItems.First();

                        var outputPoint = bestItem.Point.SRID == OutputSRID
                            ? bestItem.Point
                            : CoordinateConverter.ConvertPoint(bestItem.Point, OutputSRID);

                        var resultModel = bestItem.OriginalModel;
                        resultModel.Geometry = outputPoint;
                        resultModel.Wkt = outputPoint.ToText();

                        topItems.Add(resultModel);

                        remainingItems = remainingItems.Where(p =>
                            p.Point.Distance(bestItem.Point) >= minDistance).ToList();
                    }

                    return topItems;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message}");
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
                        
                        continue;
                    }

                    
                    geometry.SRID = geometry.SRID == -1 ? 4326 : geometry.SRID;

                    
                    var convertedPoint = geometry.SRID == TargetSRID
                        ? geometry
                        : CoordinateConverter.ConvertPoint(geometry, TargetSRID);

                    points.Add(convertedPoint);
                }
                catch (Exception ex)
                {
                    throw new Exception($"{ex.Message}");
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
                    throw new Exception($"{ex.Message}");
                }
            }
            return results;
        }


    

        private Envelope CalculateEnvelope(List<Point> points)
        {
            var envelope = new Envelope();
            foreach (var point in points)
            {
                envelope.ExpandToInclude(point.Coordinate);
            }

            
            envelope.ExpandBy(envelope.Width * 0.1, envelope.Height * 0.1);

            return envelope;
        }

        public void Dispose()
        {}
    }
}