using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using ClothPreview.Models;

namespace ClothPreview.Services
{
    /// Guarda/lee los cuerpos base ya procesados, para no leer GTA V cada vez.
    public static class BodyCache
    {
        public static void Save(string path, Dictionary<string, List<MeshPart>> body)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(body.Count);
                foreach (var kv in body)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.Count);
                    foreach (var p in kv.Value)
                    {
                        w.Write(p.Positions.Length);
                        foreach (var v in p.Positions) { w.Write((float)v.X); w.Write((float)v.Y); w.Write((float)v.Z); }
                        w.Write(p.Normals != null);
                        if (p.Normals != null) foreach (var n in p.Normals) { w.Write((float)n.X); w.Write((float)n.Y); w.Write((float)n.Z); }
                        w.Write(p.TexCoords != null);
                        if (p.TexCoords != null) foreach (var t in p.TexCoords) { w.Write((float)t.X); w.Write((float)t.Y); }
                        w.Write(p.Indices.Length);
                        foreach (var i in p.Indices) w.Write(i);
                    }
                }
            }
        }

        public static Dictionary<string, List<MeshPart>> Load(string path)
        {
            var body = new Dictionary<string, List<MeshPart>>();
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                int groups = r.ReadInt32();
                for (int g = 0; g < groups; g++)
                {
                    string key = r.ReadString();
                    int parts = r.ReadInt32();
                    var list = new List<MeshPart>();
                    for (int k = 0; k < parts; k++)
                    {
                        var p = new MeshPart();
                        int vc = r.ReadInt32();
                        p.Positions = new Point3D[vc];
                        for (int i = 0; i < vc; i++) p.Positions[i] = new Point3D(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        if (r.ReadBoolean())
                        {
                            p.Normals = new Vector3D[vc];
                            for (int i = 0; i < vc; i++) p.Normals[i] = new Vector3D(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        }
                        if (r.ReadBoolean())
                        {
                            p.TexCoords = new Point[vc];
                            for (int i = 0; i < vc; i++) p.TexCoords[i] = new Point(r.ReadSingle(), r.ReadSingle());
                        }
                        int ic = r.ReadInt32();
                        p.Indices = new int[ic];
                        for (int i = 0; i < ic; i++) p.Indices[i] = r.ReadInt32();
                        list.Add(p);
                    }
                    body[key] = list;
                }
            }
            return body;
        }
    }
}
