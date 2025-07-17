using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        private double[,] CalculateDifferenceMap(double[][] population, List<Point> existingBins, double cellSize)
        {
            int width = population.Length;
            int height = population[0].Length;
            var trashbinHeatmap = new double[width, height];

            foreach (var bin in existingBins)
            {
                int x = (int)(bin.X / cellSize);
                int y = (int)(bin.Y / cellSize);
                if (x >= 0 && x < width && y >= 0 && y < height)
                    trashbinHeatmap[x, y] += 1;
            }

            trashbinHeatmap = ApplyBlur(trashbinHeatmap, radius: 3);

            var difference = new double[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    difference[x, y] = population[x][y] - trashbinHeatmap[x, y];

            return difference;
        }

        private List<Point> FindPeaks(double[,] map, double cellSize, int count, double minDistance)
        {
            var peaks = new List<Point>();
            int minDistInCells = (int)(minDistance / cellSize);

            Console.WriteLine($"Searching for {count} peaks...");
            for (int i = 0; i < count; i++)
            {
                double maxVal = 0;
                int maxX = 0, maxY = 0;

                
                for (int x = 0; x < map.GetLength(0); x++)
                {
                    for (int y = 0; y < map.GetLength(1); y++)
                    {
                        Console.WriteLine($"Map[{x},{y}] = {map[x, y]}");
                        if (map[x, y] > maxVal)
                        {
                            maxVal = map[x, y];
                            maxX = x;
                            maxY = y;
                        }
                    }
                }

                if (maxVal <= 0)
                {
                    Console.WriteLine("No more peaks found.");
                    break;
                }

                var point = new Point(
                    maxX * cellSize + cellSize / 2,
                    maxY * cellSize + cellSize / 2);
                peaks.Add(point);
                Console.WriteLine($"Added peak {i + 1}: {point}");

               
                for (int x = Math.Max(0, maxX - minDistInCells);
                     x <= Math.Min(map.GetLength(0) - 1, maxX + minDistInCells); x++)
                {
                    for (int y = Math.Max(0, maxY - minDistInCells);
                         y <= Math.Min(map.GetLength(1) - 1, maxY + minDistInCells); y++)
                    {
                        map[x, y] = 0;
                    }
                }
            }

            return peaks;
        }

        private double[,] ApplyBlur(double[,] input, int radius)
        {
            int w = input.GetLength(0), h = input.GetLength(1);
            var output = new double[w, h];

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int dx = -radius; dx <= radius; dx++)
                        for (int dy = -radius; dy <= radius; dy++)
                            if (x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h)
                            {
                                sum += input[x + dx, y + dy];
                                count++;
                            }
                    output[x, y] = sum / count;
                }

            return output;
        }

        public List<Point> Optimize(double[][] population, List<Point> existingBins, double cellSize, int binCount, double minDistance)
        {
            var differenceMap = CalculateDifferenceMap(population, existingBins, cellSize);
            return FindPeaks(differenceMap, cellSize, binCount, minDistance);
        }
    }
}