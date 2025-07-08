using NetTopologySuite.Geometries;
using System.Text.Json.Serialization;

public record PointDto(int Id, string? Name, string Wkt);
