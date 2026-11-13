using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using ClothPreview.Models;

namespace ClothPreview.Services
{
    /// Los accesorios (sombreros, gafas, relojes…) se guardan en su propio espacio y el juego los
    /// engancha a un hueso. Aquí se calcula un punto de enganche aproximado según el cuerpo.
    /// Se puede afinar a mano con los deslizadores.
    public static class PropAnchor
    {
        public static Vector3D For(string comp, Dictionary<string, List<MeshPart>> body)
        {
            if (body == null || comp == null) return new Vector3D();
            switch (comp.ToLowerInvariant())
            {
                case "p_head": case "p_eyes": case "p_ears": case "p_mouth": return Head(body);
                case "p_lhand": case "p_lwrist": return Side(body, "hand", true, true);
                case "p_rhand": case "p_rwrist": return Side(body, "hand", false, true);
                case "p_lfoot": return Side(body, "feet", true, false);
                case "p_rfoot": return Side(body, "feet", false, false);
                default: return new Vector3D();
            }
        }

        static List<Point3D> Pts(Dictionary<string, List<MeshPart>> body, string key)
        {
            var l = new List<Point3D>();
            List<MeshPart> parts;
            if (body.TryGetValue(key, out parts)) foreach (var p in parts) l.AddRange(p.Positions);
            return l;
        }

        static Vector3D Head(Dictionary<string, List<MeshPart>> body)
        {
            var pts = Pts(body, "head");
            if (pts.Count == 0) return new Vector3D();
            double minZ = pts.Min(p => p.Z), maxZ = pts.Max(p => p.Z);
            double cx = (pts.Min(p => p.X) + pts.Max(p => p.X)) / 2;
            double cy = (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2;
            return new Vector3D(cx, cy, minZ + 0.38 * (maxZ - minZ));
        }

        static Vector3D Side(Dictionary<string, List<MeshPart>> body, string key, bool left, bool inner)
        {
            var pts = Pts(body, key);
            if (pts.Count == 0) return new Vector3D();
            double cx = (pts.Min(p => p.X) + pts.Max(p => p.X)) / 2;
            var side = pts.Where(p => left ? p.X < cx : p.X > cx).ToList();
            if (side.Count == 0) return new Vector3D();
            double x = inner ? (left ? side.Max(p => p.X) : side.Min(p => p.X))
                             : (side.Min(p => p.X) + side.Max(p => p.X)) / 2;
            double y = (side.Min(p => p.Y) + side.Max(p => p.Y)) / 2;
            double z = (side.Min(p => p.Z) + side.Max(p => p.Z)) / 2;
            return new Vector3D(x, y, z);
        }
    }
}
