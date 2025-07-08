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
        private string? _wkt;
        [JsonIgnore]
        public Geometry? Geometry { get; set; }

        [NotMapped]
       
        public string? Wkt
        {
            get => Geometry?.AsText();  // GET: veritabanındaki geometriyi döner
            set => _wkt = value;        // SET: gelen veriyi geçici olarak saklar
        }

        public string? GetRawWkt() => _wkt;
    }
}

