using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Pfim;

namespace ClothPreview.Services
{
    /// .ytd -> BitmapSource (textura difusa).
    public static class TextureLoader
    {
        public static BitmapSource Load(string ytdPath)
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(ytdPath), 13);   // 13 = versión de recurso de YTD

            var texs = ytd.TextureDict?.Textures?.data_items;
            if (texs == null || texs.Length == 0) return null;

            var tex = texs.FirstOrDefault(t => t.Name != null && t.Name.ToLowerInvariant().Contains("diff")) ?? texs[0];
            byte[] dds = DDSIO.GetDDSFile(tex);
            if (dds == null) return null;

            using (var img = Pfimage.FromStream(new MemoryStream(dds)))
            {
                PixelFormat pf;
                if (img.Format == ImageFormat.Rgba32) pf = PixelFormats.Bgr32;    // BGRA pero SIN transparencia: en GTA el alfa guarda otros datos
                else if (img.Format == ImageFormat.Rgb24) pf = PixelFormats.Bgr24;
                else return null;   // formatos raros (p. ej. algunos BC7): ver README

                var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, pf, null, img.Data, img.Stride);
                bmp.Freeze();
                return bmp;
            }
        }
    }
}
