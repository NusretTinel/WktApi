using APP.Interface;
using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NetTopologySuite.IO;

namespace SimplePointApplication.Entity
{
    [Table("test_geog")]
    public class WktModel : IEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        [JsonIgnore]
        public Geometry Geometry { get; set; } = null!;

        [NotMapped]
        public string Wkt
        {
            get => Geometry != null ? Geometry.AsText() : "";
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var reader = new WKTReader();
                    Geometry = reader.Read(value);
                }
            }
        }
    }
}
