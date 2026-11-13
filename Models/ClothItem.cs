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

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "jbib", "Camiseta / torso" }, { "uppr", "Brazos / torso" }, { "lowr", "Pantalón" }, { "feet", "Zapatos" },
            { "accs", "Accesorio (cadena, corbata)" }, { "task", "Chaleco / armadura" }, { "decl", "Calcomanía" },
            { "teef", "Dientes" }, { "berd", "Barba / máscara" }, { "hair", "Pelo" }, { "hand", "Manos / guantes" },
            { "head", "Cabeza" }, { "p_head", "Sombrero / gorra" }, { "p_eyes", "Gafas" }, { "p_ears", "Aretes / orejas" },
            { "p_mouth", "Boca" }, { "p_lhand", "Mano izquierda" }, { "p_rhand", "Mano derecha" },
            { "p_lwrist", "Reloj / muñeca izq." }, { "p_rwrist", "Muñeca derecha" }, { "p_hip", "Cadera" },
            { "p_lfoot", "Pie izquierdo" }, { "p_rfoot", "Pie derecho" }
        };

        public string FriendlyName { get { string n; return Names.TryGetValue(Component ?? "", out n) ? n : Component; } }
        public string Code { get { return Component + "_" + Index.ToString("000") + (string.IsNullOrEmpty(Suffix) ? "" : "_" + Suffix); } }
        public string Detail { get { return Code + " · " + Textures.Count + (Textures.Count == 1 ? " textura" : " texturas"); } }
        public string GenderGlyph { get { return Gender == Gender.Male ? "♂" : Gender == Gender.Female ? "♀" : ""; } }

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
