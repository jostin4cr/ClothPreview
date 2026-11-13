using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ClothPreview.Models;
using CodeWalker.GameFiles;

namespace ClothPreview.Services
{
    /// Lee los cuerpos base (mp_m_freemode_01 / mp_f_freemode_01) del GTA V instalado
    /// y los guarda en un archivo de caché junto al programa. Se hace una sola vez.
    public static class BodyImporter
    {
        // head_000_r, uppr_000_u, lowr_000_r, hand_000_u, feet_000_u... (acepta sufijo u o r)
        static readonly Regex StemRx = new Regex(@"^(head|uppr|lowr|hand|feet)_000_[ur]$", RegexOptions.Compiled);

        public static string CachePath(Gender g)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, g == Gender.Female ? "body_female.bin" : "body_male.bin");
        }

        public static Dictionary<string, List<MeshPart>> LoadCached(Gender g)
        {
            string p = CachePath(g);
            if (!File.Exists(p)) return null;
            try { return BodyCache.Load(p); } catch { return null; }
        }

        static bool InPedFolder(string path, Gender g)
        {
            string id = g == Gender.Male ? "mp_m_freemode_01" : "mp_f_freemode_01";
            // ...\mp_m_freemode_01\x.ydd   o   ..._mp_m_freemode_01.rpf\x.ydd   (se excluyen los packs DLC y los props "_p")
            return Regex.IsMatch(path, @"(^|[\\_])" + id + @"(\.rpf)?\\");
        }

        public static void ImportFromGta(string gtaFolder, Action<string> status)
        {
            var sw = Stopwatch.StartNew();
            Action<string> throttled = s => { if (sw.ElapsedMilliseconds > 300) { sw.Restart(); status(s); } };

            status("Cargando claves de GTA V…");
            RpfManager man = CreateManager(gtaFolder, throttled);

            status("Buscando los cuerpos base…");
            var genders = new[] { Gender.Male, Gender.Female };
            var found = new Dictionary<Gender, Dictionary<string, RpfEntry>>();
            var allStems = new Dictionary<Gender, SortedSet<string>>();
            foreach (var g in genders) { found[g] = new Dictionary<string, RpfEntry>(); allStems[g] = new SortedSet<string>(); }

            foreach (RpfFile rpf in man.AllRpfs)
            {
                if (rpf.AllEntries == null) continue;
                foreach (RpfEntry entry in rpf.AllEntries)
                {
                    string name = entry.NameLower;
                    if (name == null || !name.EndsWith(".ydd")) continue;
                    string stem = name.Substring(0, name.Length - 4);
                    string path = (entry.Path ?? "").Replace('/', '\\').ToLowerInvariant();

                    foreach (var g in genders)
                    {
                        if (!InPedFolder(path, g)) continue;
                        allStems[g].Add(stem);
                        if (StemRx.IsMatch(stem))
                        {
                            string comp = stem.Substring(0, 4);
                            if (!found[g].ContainsKey(comp)) found[g][comp] = entry;
                        }
                    }
                }
            }

            // Archivo de diagnóstico (por si falta alguna pieza)
            try
            {
                var lines = new List<string>();
                foreach (var g in genders)
                    lines.Add((g == Gender.Male ? "HOMBRE: " : "MUJER: ") + string.Join(", ", allStems[g].Take(300)));
                File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "body_debug.txt"), lines.ToArray());
            }
            catch { }

            var summary = new List<string>();
            foreach (var g in genders)
            {
                if (found[g].Count == 0)
                    throw new Exception("No encontré el cuerpo " + (g == Gender.Male ? "masculino" : "femenino") +
                                        ". Comprueba que la carpeta sea de GTA V (no Enhanced).");

                status("Procesando cuerpo " + (g == Gender.Male ? "masculino" : "femenino") + "…");
                var body = new Dictionary<string, List<MeshPart>>();
                foreach (var kv in found[g])
                {
                    var ydd = man.GetFile<YddFile>(kv.Value);
                    if (ydd == null) continue;
                    var parts = RageMeshLoader.FromYdd(ydd);
                    if (parts.Count > 0) body[kv.Key] = parts;
                }
                if (body.Count == 0) throw new Exception("No pude leer la malla del cuerpo.");
                BodyCache.Save(CachePath(g), body);
                summary.Add((g == Gender.Male ? "Hombre" : "Mujer") + ": " + string.Join("·", body.Keys.OrderBy(k => k)));
            }
            status("Cuerpos importados. " + string.Join("  |  ", summary));
        }

        // --- Llamadas a CodeWalker tolerantes a cambios de firma entre versiones ---

        static RpfManager CreateManager(string folder, Action<string> status)
        {
            var load = typeof(GTA5Keys).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                       .First(m => m.Name == "LoadFromPath");
            Invoke(load, null, folder, status);

            var man = new RpfManager();
            var init = typeof(RpfManager).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                         .First(m => m.Name == "Init" && m.GetParameters().Length > 0 &&
                                                     m.GetParameters()[0].ParameterType == typeof(string));
            Invoke(init, man, folder, status);
            return man;
        }

        static void Invoke(MethodInfo m, object target, string folder, Action<string> status)
        {
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            bool pathSet = false;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.ParameterType == typeof(string)) { args[i] = pathSet ? "" : folder; pathSet = true; }
                else if (p.ParameterType == typeof(bool))
                    args[i] = (p.Name ?? "").ToLowerInvariant().Contains("index") ? false : (p.HasDefaultValue ? p.DefaultValue : false);
                else if (p.ParameterType == typeof(Action<string>)) args[i] = status;
                else args[i] = p.HasDefaultValue ? p.DefaultValue : null;
            }
            try { m.Invoke(target, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
    }
}
