using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Media3D;

namespace ClothPreview.Models
{
    public enum Gender { Unknown, Male, Female }

    public class TextureVariant
    {
        public string Label { get; set; }
        public string Path { get; set; }
    }

    public class ClothItem
    {
        public string Component { get; set; }   // jbib, uppr, lowr, feet...
        public int Index { get; set; }
        public string Suffix { get; set; }      // u (universal) o r (race)
        public string YddPath { get; set; }
        public Gender Gender { get; set; }
        public List<TextureVariant> Textures { get; } = new List<TextureVariant>();
        public string Display => $"{Component}_{Index:000}_{Suffix}  ({Textures.Count} tex)" + (Gender == Gender.Male ? "  ♂" : Gender == Gender.Female ? "  ♀" : "");
    }

    public class MeshPart
    {
        public Point3D[] Positions;
        public Vector3D[] Normals;      // puede ser null
        public Point[] TexCoords;       // puede ser null
        public int[] Indices;
    }
}
