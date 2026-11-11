using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;
using ClothPreview.Models;
using CodeWalker.GameFiles;

namespace ClothPreview.Services
{
    /// ADAPTADOR CodeWalker -> MeshPart.
    /// Todo lo que depende de la API de CodeWalker.Core está SOLO en este archivo
    /// (y en TextureLoader). Si tu copia de CodeWalker nombra algo distinto,
    /// se corrige aquí sin tocar el resto del programa.
    public static class RageMeshLoader
    {
        // Tamaño en bytes de cada VertexComponentType de RAGE:
        // 0 Nothing, 1 Half2, 2 Float, 3 Half4, 4 FloatUnk, 5 Float2, 6 Float3, 7 Float4, 8 UByte4, 9 Colour, 10 Dec3N
        static readonly int[] CompSize = { 0, 4, 4, 8, 4, 8, 12, 16, 4, 4, 4 };

        const int SlotPosition = 0, SlotNormal = 3, SlotTexCoord0 = 6;

        public static List<MeshPart> Load(string path)
        {
            var data = File.ReadAllBytes(path);
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, data, 165);   // 165 = versión de recurso de YDD

            var parts = new List<MeshPart>();
            if (ydd.Drawables == null) return parts;

            foreach (var drawable in ydd.Drawables)
            {
                // Solo LOD alto, para no duplicar geometría.
                var models = drawable.DrawableModelsHigh?.data_items ?? drawable.AllModels;
                if (models == null) continue;

                foreach (var model in models)
                {
                    if (model?.Geometries == null) continue;
                    foreach (var geom in model.Geometries)
                    {
                        var part = ReadGeometry(geom);
                        if (part != null) parts.Add(part);
                    }
                }
            }
            return parts;
        }

        static MeshPart ReadGeometry(DrawableGeometry geom)
        {
            var vb = geom.VertexBuffer;
            var ib = geom.IndexBuffer;
            if (vb == null || ib?.Indices == null || vb.Data1 == null) return null;

            byte[] buf = vb.Data1;
            int stride = vb.VertexStride;
            int count = (int)vb.VertexCount;
            if (stride <= 0 || count <= 0) return null;

            // Offsets de cada componente a partir de Flags (bit por slot) y Types (4 bits por slot)
            ulong flags = vb.Info.Flags;
            ulong types = vb.Info.Types;
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
            if (offset[SlotPosition] < 0) return null;

            var part = new MeshPart
            {
                Positions = new Point3D[count],
                Normals = (offset[SlotNormal] >= 0 && type[SlotNormal] == 6) ? new Vector3D[count] : null,
                TexCoords = offset[SlotTexCoord0] >= 0 ? new Point[count] : null
            };

            for (int i = 0; i < count; i++)
            {
                int b = i * stride;
                part.Positions[i] = new Point3D(
                    BitConverter.ToSingle(buf, b + offset[SlotPosition]),
                    BitConverter.ToSingle(buf, b + offset[SlotPosition] + 4),
                    BitConverter.ToSingle(buf, b + offset[SlotPosition] + 8));

                if (part.Normals != null)
                {
                    int o = b + offset[SlotNormal];
                    part.Normals[i] = new Vector3D(
                        BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4), BitConverter.ToSingle(buf, o + 8));
                }

                if (part.TexCoords != null)
                {
                    int o = b + offset[SlotTexCoord0];
                    if (type[SlotTexCoord0] == 5)        // Float2
                        part.TexCoords[i] = new Point(BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4));
                    else if (type[SlotTexCoord0] == 1)   // Half2
                        part.TexCoords[i] = new Point(Half(BitConverter.ToUInt16(buf, o)), Half(BitConverter.ToUInt16(buf, o + 2)));
                    else
                        part.TexCoords[i] = new Point(0, 0);
                }
            }

            // Solo triángulos con índices válidos
            var idx = new List<int>(ib.Indices.Length);
            for (int i = 0; i + 2 < ib.Indices.Length; i += 3)
            {
                int a = ib.Indices[i], c = ib.Indices[i + 1], d = ib.Indices[i + 2];
                if (a < count && c < count && d < count) { idx.Add(a); idx.Add(c); idx.Add(d); }
            }
            part.Indices = idx.ToArray();
            return part.Indices.Length > 0 ? part : null;
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
