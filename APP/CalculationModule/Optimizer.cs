using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OpenCvSharp;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using Point = NetTopologySuite.Geometries.Point;

namespace SimplePointApplication.Tools
{
    public class Optimizer
    {
        public const int MaxGridDimension = 2147483647;
        private interface IPopulationDataSource : IDisposable
        {
            double[,] GetPopulationDataForArea(int width, int height, Polygon bounds);
        }

        

            public class GdalPopulationDataSource : IPopulationDataSource
            {
                private readonly Dataset _dataset;
                private readonly double[] _geoTransform = new double[6];

                public GdalPopulationDataSource(string path)
                {
                    try
                    {
                        _dataset = Gdal.Open(path, Access.GA_ReadOnly) ??
                            throw new Exception("GDAL returned null dataset");
                        _dataset.GetGeoTransform(_geoTransform);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"GDAL initialization failed: {ex.Message}");
                    }
                }

                public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
                {
                    var heatmap = new double[width, height];
                    var env = bounds.EnvelopeInternal;

                    Band band = _dataset.GetRasterBand(1);
                    double noDataValue = 0;
                    int hasVal;
                    band.GetNoDataValue(out noDataValue, out hasVal);
                    if (hasVal == 0) noDataValue = 0;

                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            double geoX = env.MinX + (env.MaxX - env.MinX) * x / width;
                            double geoY = env.MinY + (env.MaxY - env.MinY) * y / height;

                            if (bounds.Contains(new Point(geoX, geoY)))
                            {
                                GeoToPixel(geoX, geoY, out int pixelX, out int pixelY);
                                if (IsInImage(pixelX, pixelY))
                                {
                                    double[] value = new double[1];
                                    band.ReadRaster(pixelX, pixelY, 1, 1, value, 1, 1, 0, 0);
                                    heatmap[x, y] = (hasVal != 0 && value[0] == noDataValue) ? 0 : value[0];
                                }
                            }
                        }
                    }
                

                return heatmap;
                }

                private void GeoToPixel(double geoX, double geoY, out int pixelX, out int pixelY)
                {
                    pixelX = (int)((geoX - _geoTransform[0]) / _geoTransform[1]);
                    pixelY = (int)((geoY - _geoTransform[3]) / _geoTransform[5]);
                }

                private bool IsInImage(int x, int y) =>
                    x >= 0 && x < _dataset.RasterXSize &&
                    y >= 0 && y < _dataset.RasterYSize;

                public void Dispose() => _dataset?.Dispose();
            }

            
            private class BitmapPopulationDataSource : IPopulationDataSource
        {
            private readonly Bitmap _image;
            private readonly Envelope _bounds;

            public BitmapPopulationDataSource(string path)
            {
                try
                {
                    _image = new Bitmap(path);
                    
                    _bounds = new Envelope(26.0, 45.0, 36.0, 42.0);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Bitmap loading failed: {ex.Message}");
                }
            }

            public double[,] GetPopulationDataForArea(int width, int height, Polygon bounds)
            {
                var heatmap = new double[width, height];
                var env = bounds?.EnvelopeInternal ?? _bounds;

                double xScale = _image.Width / _bounds.Width;
                double yScale = _image.Height / _bounds.Height;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        double geoX = env.MinX + env.Width * x / width;
                        double geoY = env.MaxY - env.Height * y / height; 

                        int imgX = (int)((geoX - _bounds.MinX) * xScale);
                        int imgY = (int)((_bounds.MaxY - geoY) * yScale);

                        if (imgX >= 0 && imgX < _image.Width && imgY >= 0 && imgY < _image.Height)
                        {
                            Color pixel = _image.GetPixel(imgX, imgY);
                            heatmap[x, y] = pixel.R; 
                        }
                    }
                }
                
                return heatmap;
            }

            public void Dispose() => _image?.Dispose();
        }

        
        public List<Point> Optimize(
            string populationDataSourcePath,
            List<Point> existingBins,
            int gridWidth,
            int gridHeight,
            double cellSize,
            int binCount,
            double minDistance,
            string polygonWkt = null)
        {
            using (var dataSource = CreateBestDataSource(populationDataSourcePath))
            {
                Polygon polygon = string.IsNullOrEmpty(polygonWkt) ?
                    CreateDefaultPolygon(gridWidth, gridHeight, cellSize) :
                    (Polygon)new WKTReader().Read(polygonWkt);

                var population = dataSource.GetPopulationDataForArea(gridWidth, gridHeight, polygon);
                var difference = CalculateDifferenceMap(population, existingBins, cellSize);
                return FindPeaks(difference, cellSize, binCount, minDistance, polygon);
            }
        }

        private IPopulationDataSource CreateBestDataSource(string path)
        {
            
            try
            {
                return new GdalPopulationDataSource(path);
            }
            catch (Exception gdalEx)
            {
                Console.WriteLine($"GDAL failed, trying Bitmap: {gdalEx.Message}");
                try
                {
                    return new BitmapPopulationDataSource(path);
                }
                catch (Exception bitmapEx)
                {
                    throw new AggregateException(
                        "All data source options failed",
                        new[] { gdalEx, bitmapEx });
                }
            }
        }

        private double[,] CalculateDifferenceMap(double[,] population, List<Point> existingBins, double cellSize)
        {
            int width = population.GetLength(0);
            int height = population.GetLength(1);
            var binHeatmap = new double[width, height];

            
            foreach (var bin in existingBins)
            {
                int x = (int)(bin.X / cellSize);
                int y = (int)(bin.Y / cellSize);
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    
                    binHeatmap[x, y] += 10; 
                }
            }

      
            binHeatmap = ApplyGaussianBlur(binHeatmap, 15, 3.0);

           
            var difference = new double[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                   
                    difference[x, y] = Math.Max(0, population[x, y] - binHeatmap[x, y] * 0.5);
                }
            }

            return difference;
        }

        private List<Point> FindPeaks(double[,] map, double cellSize, int count, double minDistance, Polygon bounds)
        {
            var peaks = new List<Point>();
            int minDistCells = (int)(minDistance / cellSize);

       
            var mapCopy = (double[,])map.Clone();

            
            NormalizeMap(mapCopy);

            for (int i = 0; i < count; i++)
            {
                (int x, int y) = FindMaxValue(mapCopy, bounds, cellSize);




                double geoX = bounds.EnvelopeInternal.MinX + (x * cellSize);  
                double geoY = bounds.EnvelopeInternal.MinY + (y * cellSize);
              

                peaks.Add(new Point(geoX, geoY) { SRID = 4326 });  

                
                for (int dx = -minDistCells; dx <= minDistCells; dx++)
                {
                    for (int dy = -minDistCells; dy <= minDistCells; dy++)
                    {
                       
                        if (dx * dx + dy * dy <= minDistCells * minDistCells)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < mapCopy.GetLength(0) &&
                                ny >= 0 && ny < mapCopy.GetLength(1))
                            {
                                
                                double distance = Math.Sqrt(dx * dx + dy * dy);
                                double reduction = 1 - (distance / minDistCells);
                                mapCopy[nx, ny] *= (1 - reduction * 0.9); 
                            }
                        }
                    }
                }
            }
           
            return peaks;
        }

        private void NormalizeMap(double[,] map)
        {
            double max = 0;
            foreach (var val in map)
            {
                if (val > max) max = val;
            }

            if (max > 0)
            {
                for (int x = 0; x < map.GetLength(0); x++)
                {
                    for (int y = 0; y < map.GetLength(1); y++)
                    {
                        map[x, y] /= max;
                    }
                }
            }
        }

        private (int x, int y) FindMaxValue(double[,] map, Polygon bounds, double cellSize)
        {
            int maxX = 0, maxY = 0;
            double maxVal = double.MinValue;

            for (int x = 0; x < map.GetLength(0); x++)
            {
                for (int y = 0; y < map.GetLength(1); y++)
                {
                    
                    if (map[x, y] < maxVal * 0.8) continue;

                   
                    double geoX = bounds.EnvelopeInternal.MinX + (x * cellSize);
                    double geoY = bounds.EnvelopeInternal.MinY + (y * cellSize);
                    var point = new Point(geoX, geoY);

                    
                    if (bounds != null && !bounds.Contains(point)) continue;

                    if (map[x, y] > maxVal)
                    {
                        maxVal = map[x, y];
                        maxX = x;
                        maxY = y;
                    }
                }
            }

            return (maxX, maxY);
        }

        private double[,] ApplyGaussianBlur(double[,] input, int kernelSize, double sigma)
        {
            using (var src = new Mat(input.GetLength(1), input.GetLength(0), MatType.CV_64FC1))
            using (var dst = new Mat())
            {
                
                for (int y = 0; y < src.Rows; y++)
                    for (int x = 0; x < src.Cols; x++)
                        src.Set(y, x, input[x, y]);

                Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(kernelSize, kernelSize), sigma);

                
                var output = new double[input.GetLength(0), input.GetLength(1)];
                for (int y = 0; y < dst.Rows; y++)
                    for (int x = 0; x < dst.Cols; x++)
                        output[x, y] = dst.Get<double>(y, x);

                return output;
            }
        }

        private Polygon CreateDefaultPolygon(int width, int height, double cellSize)
        {
            return new Polygon(new LinearRing(new[]
            {
                new Coordinate(0, 0),
                new Coordinate(width * cellSize, 0),
                new Coordinate(width * cellSize, height * cellSize),
                new Coordinate(0, height * cellSize),
                new Coordinate(0, 0)
            }));
        }
    }
}