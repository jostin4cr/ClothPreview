using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Media3D;
using ClothPreview.Models;
using CodeWalker.GameFiles;

namespace ClothPreview.Services
{
    /// ADAPTADOR CodeWalker -> MeshPart.
    /// Todo lo que depende de la API de CodeWalker.Core está en este archivo y en TextureLoader/BodyImporter.
    public static class RageMeshLoader
    {
        // Tamaño en bytes de cada VertexComponentType de RAGE:
        // 0 Nothing, 1 Half2, 2 Float, 3 Half4, 4 FloatUnk, 5 Float2, 6 Float3, 7 Float4, 8 UByte4, 9 Colour, 10 Dec3N
        static readonly int[] CompSize = { 0, 4, 4, 8, 4, 8, 12, 16, 4, 4, 4 };
        const int SlotPosition = 0, SlotNormal = 3, SlotTexCoord0 = 6;

        public static string LastSource = "";
        public static string LastDiag = "";
        static readonly List<string> Diag = new List<string>();

        public static List<MeshPart> Load(string path)
        {
            var data = File.ReadAllBytes(path);
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, data, 165);   // 165 = versión de recurso de YDD
            return FromYdd(ydd);
        }

        public static List<MeshPart> FromYdd(YddFile ydd)
        {
            Diag.Clear();
            var parts = new List<MeshPart>();
            if (ydd.Drawables == null) { Diag.Add("el archivo no tiene drawables"); LastDiag = string.Join(" | ", Diag); return parts; }

            foreach (var drawable in ydd.Drawables)
            {
                int score;
                var models = GetModels(drawable, out score);
                if (models == null) { Diag.Add("sin modelos"); continue; }

                var dparts = new List<MeshPart>();
                foreach (var model in models)
                {
                    if (model == null || model.Geometries == null) { Diag.Add("modelo sin geometrías"); continue; }
                    foreach (var geom in model.Geometries)
                    {
                        try
                        {
                            string why;
                            var part = ReadGeometry(geom, out why);
                            if (part != null) dparts.Add(part);
                            else if (why != null) Diag.Add("geometría omitida: " + why);
                        }
                        catch (Exception ex)
                        {
                            Diag.Add("error en geometría: " + ex.GetType().Name + " " + ex.Message);
                        }
                    }
                }
                parts.AddRange(score < 3 ? DropLodDuplicates(dparts) : dparts);
            }
            LastDiag = string.Join(" | ", Diag.Take(3));
            return parts;
        }

        static MeshPart ReadGeometry(DrawableGeometry geom, out string why)
        {
            why = null;
            var vb = geom.VertexBuffer;
            var ib = geom.IndexBuffer;
            if (vb == null) { why = "sin buffer de vértices"; return null; }
            if (ib == null || ib.Indices == null) { why = "sin índices"; return null; }

            byte[] buf = GetBytes(vb.Data1);
            if (buf == null) buf = GetBytes(ReadByName(vb, "Data2"));
            if (buf == null) { why = "sin datos de vértices"; return null; }

            int stride = vb.VertexStride;
            int count = (int)vb.VertexCount;
            if (stride <= 0) { why = "stride 0"; return null; }
            if ((long)stride * count > buf.Length) count = buf.Length / stride;   // por si el contador no coincide
            if (count <= 0) { why = "0 vértices"; return null; }

            // Offsets de cada componente a partir de Flags (bit por slot) y Types (4 bits por slot)
            ulong flags = Convert.ToUInt64(vb.Info.Flags);
            ulong types = Convert.ToUInt64(vb.Info.Types);
            var offset = new int[16];
            var type = new int[16];
            int off = 0;
            for (int slot = 0; slot < 16; slot++)
            {
                if (((flags >> slot) & 1UL) == 0) { offset[slot] = -1; continue; }
                int t = (int)((types >> (4 * slot)) & 0xF);
                offset[slot] = off;
                type[slot] = t;
                off += t < CompSize.Length ? CompSize[t] : 0;
            }
            if (offset[SlotPosition] < 0 || offset[SlotPosition] + 12 > stride) { why = "sin posición válida"; return null; }

            bool okNormal = offset[SlotNormal] >= 0 &&
                            ((type[SlotNormal] == 6 && offset[SlotNormal] + 12 <= stride) ||
                             (type[SlotNormal] == 10 && offset[SlotNormal] + 4 <= stride));
            bool okUv = offset[SlotTexCoord0] >= 0 &&
                        ((type[SlotTexCoord0] == 5 && offset[SlotTexCoord0] + 8 <= stride) ||
                         (type[SlotTexCoord0] == 1 && offset[SlotTexCoord0] + 4 <= stride));

            var part = new MeshPart
            {
                Positions = new Point3D[count],
                Normals = okNormal ? new Vector3D[count] : null,
                TexCoords = okUv ? new Point[count] : null
            };

            for (int i = 0; i < count; i++)
            {
                int b = i * stride;
                int po = b + offset[SlotPosition];
                part.Positions[i] = new Point3D(
                    BitConverter.ToSingle(buf, po), BitConverter.ToSingle(buf, po + 4), BitConverter.ToSingle(buf, po + 8));

                if (okNormal)
                {
                    int o = b + offset[SlotNormal];
                    if (type[SlotNormal] == 6)
                        part.Normals[i] = new Vector3D(BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4), BitConverter.ToSingle(buf, o + 8));
                    else
                    {
                        uint v = BitConverter.ToUInt32(buf, o);   // Dec3N: 3 x 10 bits con signo
                        part.Normals[i] = new Vector3D(Snorm10(v), Snorm10(v >> 10), Snorm10(v >> 20));
                    }
                }

                if (okUv)
                {
                    int o = b + offset[SlotTexCoord0];
                    if (type[SlotTexCoord0] == 5)
                        part.TexCoords[i] = new Point(BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4));
                    else
                        part.TexCoords[i] = new Point(Half(BitConverter.ToUInt16(buf, o)), Half(BitConverter.ToUInt16(buf, o + 2)));
                }
            }

            // Solo triángulos con índices válidos
            var idx = new List<int>(ib.Indices.Length);
            for (int i = 0; i + 2 < ib.Indices.Length; i += 3)
            {
                int a = ib.Indices[i], c = ib.Indices[i + 1], d = ib.Indices[i + 2];
                if (a < count && c < count && d < count) { idx.Add(a); idx.Add(c); idx.Add(d); }
            }
            if (idx.Count == 0) { why = "sin triángulos válidos"; return null; }
            part.Indices = idx.ToArray();
            return part;
        }

        // --- Búsqueda tolerante a cambios de nombre en CodeWalker ---

        static object ReadMember(MemberInfo m, object target)
        {
            try
            {
                var pi = m as PropertyInfo;
                var fi = m as FieldInfo;
                if (pi != null && pi.GetIndexParameters().Length == 0) return pi.GetValue(target, null);
                if (fi != null) return fi.GetValue(target);
            }
            catch { }
            return null;
        }

        static object ReadByName(object target, string name)
        {
            if (target == null) return null;
            var m = target.GetType().GetMember(name, BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
            return m == null ? null : ReadMember(m, target);
        }

        /// Busca los modelos del LOD alto. score: 4 = anidado "High", 3 = miembro "High",
        /// 2 = otro, 1 = AllModels (mezcla todos los LOD), 0 = LOD bajo.
        static DrawableModel[] GetModels(Drawable d, out int bestScore)
        {
            DrawableModel[] best = null;
            bestScore = -1;
            string src = "";
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;

            foreach (var m in d.GetType().GetMembers(F))
            {
                object val = ReadMember(m, d);
                if (val == null) continue;
                string n = m.Name.ToLowerInvariant();

                var arr = AsModels(val);
                if (arr != null && arr.Length > 0)
                {
                    int score = n.Contains("high") ? 3
                              : (n.Contains("med") || n.Contains("low")) ? 0
                              : n == "allmodels" ? 1 : 2;
                    if (score > bestScore) { best = arr; bestScore = score; src = m.Name; }
                    continue;
                }

                var t = val.GetType();
                if (val is string || t.IsPrimitive || t.IsEnum || val is Array) continue;
                if (t.Namespace != "CodeWalker.GameFiles") continue;
                foreach (var nm in t.GetMembers(F))
                {
                    string nn = nm.Name.ToLowerInvariant();
                    if (!(nn == "hi" || nn.Contains("high"))) continue;
                    var inner = AsModels(ReadMember(nm, val));
                    if (inner != null && inner.Length > 0 && 4 > bestScore)
                    {
                        best = inner; bestScore = 4; src = m.Name + "." + nm.Name;
                    }
                }
            }
            LastSource = (src == "" ? "ninguna" : src) + " [nivel " + bestScore + "]";
            return best;
        }

        static DrawableModel[] AsModels(object val)
        {
            if (val == null) return null;
            var direct = val as DrawableModel[];
            if (direct != null) return direct;
            var t = val.GetType();
            object inner = null;
            var f = t.GetField("data_items", BindingFlags.Public | BindingFlags.Instance);
            if (f != null) inner = f.GetValue(val);
            else
            {
                var p = t.GetProperty("data_items", BindingFlags.Public | BindingFlags.Instance);
                if (p != null) inner = p.GetValue(val, null);
            }
            return inner as DrawableModel[];
        }

        static byte[] GetBytes(object o)
        {
            if (o == null) return null;
            var direct = o as byte[];
            if (direct != null) return direct;
            foreach (var m in o.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                var bytes = ReadMember(m, o) as byte[];
                if (bytes != null && bytes.Length > 0) return bytes;
            }
            return null;
        }

        // Si no se pudo aislar el LOD alto, quita las copias casi idénticas (LOD medio/bajo).
        static List<MeshPart> DropLodDuplicates(List<MeshPart> list)
        {
            var kept = new List<MeshPart>();
            foreach (var p in list.OrderByDescending(x => x.Indices.Length))
            {
                var b = Bounds(p);
                bool dup = false;
                foreach (var k in kept)
                    if (Similar(b, Bounds(k))) { dup = true; break; }
                if (!dup) kept.Add(p);
            }
            return kept;
        }

        static Rect3D Bounds(MeshPart p)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
            double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            foreach (var v in p.Positions)
            {
                if (v.X < x0) x0 = v.X; if (v.X > x1) x1 = v.X;
                if (v.Y < y0) y0 = v.Y; if (v.Y > y1) y1 = v.Y;
                if (v.Z < z0) z0 = v.Z; if (v.Z > z1) z1 = v.Z;
            }
            return new Rect3D(x0, y0, z0, x1 - x0, y1 - y0, z1 - z0);
        }

        static double Ratio(double a, double b)
        {
            double hi = Math.Max(a, b), lo = Math.Min(a, b);
            return hi < 1e-6 ? 1 : lo / hi;
        }

        static bool Similar(Rect3D a, Rect3D b)
        {
            double m = Math.Max(Math.Max(b.SizeX, b.SizeY), b.SizeZ);
            if (m <= 0) return false;
            return Ratio(a.SizeX, b.SizeX) > 0.85 && Ratio(a.SizeY, b.SizeY) > 0.85 && Ratio(a.SizeZ, b.SizeZ) > 0.85
                && Math.Abs((a.X + a.SizeX / 2) - (b.X + b.SizeX / 2)) < 0.1 * m
                && Math.Abs((a.Y + a.SizeY / 2) - (b.Y + b.SizeY / 2)) < 0.1 * m
                && Math.Abs((a.Z + a.SizeZ / 2) - (b.Z + b.SizeZ / 2)) < 0.1 * m;
        }

        static double Snorm10(uint v)
        {
            int s = (int)(v & 0x3FF);
            if (s >= 512) s -= 1024;
            return s / 511.0;
        }

        static double Half(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 0x1F, m = h & 0x3FF;
            double v = e == 0 ? m / 1024.0 * Math.Pow(2, -14)
                     : e == 31 ? (m == 0 ? double.PositiveInfinity : double.NaN)
                     : (1 + m / 1024.0) * Math.Pow(2, e - 15);
            return s == 1 ? -v : v;
        }
    }
}
