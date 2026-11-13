using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ClothPreview.Models
{
    public enum Gender { Unknown, Male, Female }

    public class TextureVariant
    {
        public string Label { get; set; }
        public string Path { get; set; }
    }

    public class PartToggle
    {
        public string Name { get; set; }
        public bool IsVisible { get; set; } = true;
    }

    public class ClothItem
    {
        public string Component { get; set; }   // jbib, uppr, lowr, feet, accs... o p_head, p_eyes (props)
        public int Index { get; set; }
        public string Suffix { get; set; }      // u / r (vacío en props)
        public string YddPath { get; set; }
        public Gender Gender { get; set; }
        public List<TextureVariant> Textures { get; } = new List<TextureVariant>();

        public bool IsProp { get { return Component != null && Component.StartsWith("p_"); } }

        public string Display
        {
            get
            {
                string name = Component + "_" + Index.ToString("000") + (string.IsNullOrEmpty(Suffix) ? "" : "_" + Suffix);
                string g = Gender == Gender.Male ? "  ♂" : Gender == Gender.Female ? "  ♀" : "";
                return name + "  (" + Textures.Count + " tex)" + g;
            }
        }

        // ---- estado de visualización ----
        public List<MeshPart> Parts;
        public List<PartToggle> Toggles = new List<PartToggle>();
        public BitmapSource Tex;
        public int VariantIndex;
        public double OffX, OffY, OffZ;     // ajuste manual (accesorios)
        public string Diag = "";
        public string Source = "";
    }

    public class MeshPart
    {
        public Point3D[] Positions;
        public Vector3D[] Normals;      // puede ser null
        public Point[] TexCoords;       // puede ser null
        public int[] Indices;
    }
}
