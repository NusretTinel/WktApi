using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace SimplePointApplication.Tools
{
    public class KMeansOptimizer
    {
        private readonly int _maxIterations;
        private readonly Random _random;

        public KMeansOptimizer(int maxIterations = 100)
        {
            _maxIterations = maxIterations;
            _random = new Random();
        }

        public List<Point> Optimize(double[][] population, List<Point> existingBins,
                                   double cellSize, int binCount, double minDistance,
                                   Polygon polygon = null)
        {
            var weightedPoints = ConvertHeatmapToPoints(population, cellSize, polygon);
            var allCentroids = new List<Point>(existingBins);
            int newBinsToAdd = binCount;

            
            if (newBinsToAdd > 0)
            {
                var newCentroids = InitializeCentroids(weightedPoints, newBinsToAdd, existingBins, minDistance);

                allCentroids.AddRange(newCentroids);

                for (int i = 0; i < _maxIterations; i++)
                {
                    var clusters = AssignPointsToClusters(weightedPoints, allCentroids);
                    var newPositions = CalculateNewCentroids(clusters);

                    bool converged = true;
                    for (int j = 0; j < allCentroids.Count; j++)
                    {
                        if (allCentroids[j].Distance(newPositions[j]) > cellSize * 0.1) // Small threshold
                        {
                            converged = false;
                            break;
                        }
                    }

                    if (converged) break;

                    allCentroids = newPositions;
                }
            }
            return allCentroids.Skip(existingBins.Count).Take(binCount).ToList();
        }

        private List<Point> InitializeCentroids(List<WeightedPoint> points, int k, List<Point> existingBins, double minDistance)
        {
            var centroids = new List<Point>();

            while (centroids.Count < k && points.Count > 0)
            {
                double totalWeight = points.Sum(p => p.Weight);
                double randomValue = _random.NextDouble() * totalWeight;

                double cumulativeWeight = 0;
                WeightedPoint selectedPoint = null;
                foreach (var point in points)
                {
                    cumulativeWeight += point.Weight;
                    if (cumulativeWeight >= randomValue)
                    {
                        selectedPoint = point;
                        break;
                    }
                }

                if (selectedPoint == null) continue;

                var newCentroid = selectedPoint.Point;
                bool isValid = true;

                
                foreach (var existing in existingBins)
                {
                    if (existing.Distance(newCentroid) < minDistance)
                    {
                        isValid = false;
                        break;
                    }
                }

                foreach (var centroid in centroids)
                {
                    if (centroid.Distance(newCentroid) < minDistance)
                    {
                        isValid = false;
                        break;
                    }
                }

                if (isValid)
                {
                    centroids.Add(newCentroid);
                    points.RemoveAll(p => p.Point.Distance(newCentroid) < minDistance);
                }
                else
                {
                    points.Remove(selectedPoint);
                }
            }

            return centroids;
        }

        private List<Cluster> AssignPointsToClusters(List<WeightedPoint> points, List<Point> centroids)
        {
            var clusters = centroids.Select(c => new Cluster { Centroid = c }).ToList();

            foreach (var point in points)
            {
                Point nearestCentroid = null;
                double minDistance = double.MaxValue;

                foreach (var centroid in centroids)
                {
                    double distance = point.Point.Distance(centroid);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestCentroid = centroid;
                    }
                }
                var cluster = clusters.First(c => c.Centroid == nearestCentroid);
                cluster.Points.Add(point);
            }

            return clusters;
        }

        private List<Point> CalculateNewCentroids(List<Cluster> clusters)
        {
            var newCentroids = new List<Point>();

            foreach (var cluster in clusters)
            {
                if (cluster.Points.Count == 0)
                {
                    newCentroids.Add(cluster.Centroid);
                    continue;
                }

                
                double totalWeight = cluster.Points.Sum(p => p.Weight);
                double sumX = 0, sumY = 0;

                foreach (var point in cluster.Points)
                {
                    sumX += point.Point.X * point.Weight;
                    sumY += point.Point.Y * point.Weight;
                }

                double newX = sumX / totalWeight;
                double newY = sumY / totalWeight;

                newCentroids.Add(new Point(newX, newY));
            }

            return newCentroids;
        }

        private List<WeightedPoint> ConvertHeatmapToPoints(double[][] population, double cellSize, Polygon polygon)
        {
            var points = new List<WeightedPoint>();
            int width = population.Length;
            int height = population[0].Length;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    double weight = population[x][y];
                    if (weight <= 0) continue;

                    double realX = x * cellSize + cellSize / 2;
                    double realY = y * cellSize + cellSize / 2;
                    var point = new Point(realX, realY);

                    if (polygon != null && !polygon.Contains(point)) continue;

                    points.Add(new WeightedPoint { Point = point, Weight = weight });
                }
            }

            return points;
        }

        private class WeightedPoint
        {
            public Point Point { get; set; }
            public double Weight { get; set; }
        }

        private class Cluster
        {
            public Point Centroid { get; set; }
            public List<WeightedPoint> Points { get; set; } = new List<WeightedPoint>();
        }
    }
}
