using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ClothPreview.Models;
using CodeWalker.GameFiles;

namespace ClothPreview.Services
{
    /// Lee los cuerpos base (mp_m_freemode_01 / mp_f_freemode_01) del GTA V instalado
    /// y los guarda en un archivo de caché junto al programa. Se hace una sola vez.
    public static class BodyImporter
    {
        static readonly string[] Wanted = { "head_000_r", "uppr_000_u", "lowr_000_u", "hand_000_r", "feet_000_u" };

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

        public static void ImportFromGta(string gtaFolder, Action<string> status)
        {
            var sw = Stopwatch.StartNew();
            Action<string> throttled = s => { if (sw.ElapsedMilliseconds > 300) { sw.Restart(); status(s); } };

            status("Cargando claves de GTA V…");
            RpfManager man = CreateManager(gtaFolder, throttled);

            status("Buscando los cuerpos base…");
            var found = new Dictionary<Gender, Dictionary<string, RpfEntry>>
            {
                { Gender.Male, new Dictionary<string, RpfEntry>() },
                { Gender.Female, new Dictionary<string, RpfEntry>() }
            };

            foreach (RpfFile rpf in man.AllRpfs)
            {
                if (rpf.AllEntries == null) continue;
                foreach (RpfEntry entry in rpf.AllEntries)
                {
                    string name = entry.NameLower;
                    if (name == null || !name.EndsWith(".ydd")) continue;
                    string stem = name.Substring(0, name.Length - 4);
                    if (!Wanted.Contains(stem)) continue;

                    string path = (entry.Path ?? "").Replace('/', '\\').ToLowerInvariant();
                    foreach (var g in new[] { Gender.Male, Gender.Female })
                    {
                        string folder = "\\" + (g == Gender.Male ? "mp_m_freemode_01" : "mp_f_freemode_01") + "\\";
                        if (path.Contains(folder) && !found[g].ContainsKey(stem)) found[g][stem] = entry;
                    }
                }
            }

            foreach (var g in new[] { Gender.Male, Gender.Female })
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
                    if (parts.Count > 0) body[kv.Key.Substring(0, 4)] = parts;
                }
                if (body.Count == 0) throw new Exception("No pude leer la malla del cuerpo.");
                BodyCache.Save(CachePath(g), body);
            }
            status("Cuerpos importados correctamente.");
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
