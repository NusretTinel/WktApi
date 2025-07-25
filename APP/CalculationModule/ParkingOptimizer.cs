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
        private const int TargetSRID = 4326; 
        private const int OutputSRID = 4326;
        private static readonly WKTReader _wktReader = new WKTReader();
        private const int MinGridDimension = 100;

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
            var result = new List<WktModel>();

            try
            {
                var candidatePoints = ParseAndConvertPoints(candidateSpots);
                if (candidatePoints.Count == 0)
                {
                    throw new Exception("No valid points could be parsed from input");
                }

                var envelope = CalculateEnvelope(candidatePoints);

               
                int gridWidth = (int)Math.Ceiling(envelope.Width / cellSize);
                int gridHeight = (int)Math.Ceiling(envelope.Height / cellSize);

                gridWidth = Math.Max(gridWidth, MinGridDimension);
                gridHeight = Math.Max(gridHeight, MinGridDimension);

             
                if (gridWidth > Optimizer.MaxGridDimension || gridHeight > Optimizer.MaxGridDimension)
                {
                    double scaleFactor = Math.Max(
                        (double)gridWidth / Optimizer.MaxGridDimension,
                        (double)gridHeight / Optimizer.MaxGridDimension);
                    gridWidth = (int)(gridWidth / scaleFactor);
                    gridHeight = (int)(gridHeight / scaleFactor);
                }

                using (var dataSource = new Optimizer.GdalPopulationDataSource(_populationDataPath))
                {
                    string envelopeText = $"POLYGON(({envelope.MinX} {envelope.MinY}, {envelope.MaxX} {envelope.MinY}, {envelope.MaxX} {envelope.MaxY}, {envelope.MinX} {envelope.MaxY}, {envelope.MinX} {envelope.MinY}))";
                    var population = dataSource.GetPopulationDataForArea(
                        gridWidth,
                        gridHeight,
                        new WKTReader().Read(envelopeText) as Polygon);

                    if (!ValidatePopulationData(population, gridWidth, gridHeight))
                    {
                        gridWidth = Math.Max(gridWidth * 2, MinGridDimension);
                        gridHeight = Math.Max(gridHeight * 2, MinGridDimension);
                        population = dataSource.GetPopulationDataForArea(
                            gridWidth,
                            gridHeight,
                            new WKTReader().Read(envelopeText) as Polygon);
                    }

                    var scoredPoints = new List<(WktModel Model, Point Point, double Score)>();

                    foreach (var (point, index) in candidatePoints.Select((p, i) => (p, i)))
                    {
                        int centerX = (int)((point.X - envelope.MinX) / cellSize);
                        int centerY = (int)((point.Y - envelope.MinY) / cellSize);

                        double score = CalculatePopulationScore(
                            population,
                            centerX,
                            centerY,
                            gridWidth,
                            gridHeight,
                            cellSize,
                            minDistance);

                        scoredPoints.Add((candidateSpots[index], point, score));
                    }

                    var selectedPoints = new List<Point>();
                    var orderedCandidates = scoredPoints.OrderByDescending(x => x.Score).ToList();

                    foreach (var candidate in orderedCandidates)
                    {
                        bool isTooClose = selectedPoints.Any(p =>
                            p.Distance(candidate.Point) < minDistance);

                        if (!isTooClose)
                        {
                            var resultModel = candidate.Model;
                            resultModel.Geometry = candidate.Point;
                            resultModel.Wkt = candidate.Point.ToText();
                            result.Add(resultModel);
                            selectedPoints.Add(candidate.Point);

                            if (result.Count >= topN) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Parking optimization failed: " + ex.Message);
            }

            return result;
        }

        private List<Point> ParseAndConvertPoints(List<WktModel> models)
        {
            var points = new List<Point>();
            foreach (var model in models)
            {
                try
                {
                    var geometry = _wktReader.Read(model.GetRawWkt()) as Point;
                    if (geometry == null) continue;

                    geometry.SRID = geometry.SRID == -1 ? 4326 : geometry.SRID;

                    points.Add(geometry);
                }
                catch
                {
                    continue;
                }
            }
            return points;
        }

        private Envelope CalculateEnvelope(List<Point> points)
        {
            if (points == null || points.Count == 0)
                return new Envelope(26, 45, 36, 42); 

            var envelope = new Envelope();
            foreach (var point in points)
            {
                envelope.ExpandToInclude(point.Coordinate);
            }

          
            envelope.ExpandBy(envelope.Width * 0.1, envelope.Height * 0.1);
            return envelope;
        }

       

        public void Dispose()
        {

        }
        private bool ValidatePopulationData(double[,] population, int gridWidth, int gridHeight)
        {
            double totalPopulation = 0;
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    totalPopulation += population[x, y];
                }
            }
            return totalPopulation > 0;
        }
        private double CalculatePopulationScore(
          double[,] population,
          int centerX,
          int centerY,
          int gridWidth,
          int gridHeight,
          double cellSize,
          double minDistance)
        {
            double score = 0;
            int radius = (int)(minDistance / cellSize);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int x = centerX + dx;
                    int y = centerY + dy;

                    if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                    {
                        double distance = Math.Sqrt(dx * dx + dy * dy) * cellSize;
                        if (distance <= minDistance)
                        {

                            double weight = Math.Exp(-2 * distance / minDistance);
                            score += population[x, y] * weight;
                        }
                    }
                }
            }

            return score;
        }


    }
}


       