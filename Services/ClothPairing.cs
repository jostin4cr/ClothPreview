using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClothPreview.Models;

namespace ClothPreview.Services
{
    /// Empareja .ydd (modelo) con .ytd (texturas) por la convención de nombres de GTA V.
    public static class ClothPairing
    {
        // jbib_000_u.ydd
        static readonly Regex YddRx = new Regex(@"^(?<c>[a-z]+)_(?<i>\d{3})_(?<s>[ur])$", RegexOptions.IgnoreCase);
        // jbib_diff_000_a_uni.ytd
        static readonly Regex YtdRx = new Regex(@"^(?<c>[a-z]+)_diff_(?<i>\d{3})_(?<v>[a-z])_(?<r>[a-z]{3})$", RegexOptions.IgnoreCase);

        // FiveM: "mp_m_freemode_01_mp_m_pack^jbib_000_u" -> "jbib_000_u"
        static string Clean(string name)
        {
            int p = name.LastIndexOf('^');
            return p >= 0 ? name.Substring(p + 1) : name;
        }

        public static List<ClothItem> Scan(IEnumerable<string> paths)
        {
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories));
                else if (File.Exists(p)) files.Add(p);
            }

            var items = new List<ClothItem>();
            var textures = new Dictionary<string, List<TextureVariant>>();

            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                string name = Clean(Path.GetFileNameWithoutExtension(f));

                if (ext == ".ydd")
                {
                    var m = YddRx.Match(name);
                    if (!m.Success) continue;   // ignora _1 (primera persona), props, etc.
                    items.Add(new ClothItem
                    {
                        Component = m.Groups["c"].Value.ToLowerInvariant(),
                        Index = int.Parse(m.Groups["i"].Value),
                        Suffix = m.Groups["s"].Value.ToLowerInvariant(),
                        YddPath = f
                    });
                }
                else if (ext == ".ytd")
                {
                    var m = YtdRx.Match(name);
                    if (!m.Success) continue;
                    string key = m.Groups["c"].Value.ToLowerInvariant() + "_" + int.Parse(m.Groups["i"].Value);
                    if (!textures.TryGetValue(key, out var list)) textures[key] = list = new List<TextureVariant>();
                    list.Add(new TextureVariant { Label = name, Path = f });
                }
            }

            foreach (var it in items)
                if (textures.TryGetValue(it.Component + "_" + it.Index, out var list))
                    it.Textures.AddRange(list.OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase));

            return items.OrderBy(i => i.Component).ThenBy(i => i.Index).ThenBy(i => i.Suffix).ToList();
        }
    }
}
