using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ClothPreview.Models;
using ClothPreview.Services;
using Microsoft.Win32;

namespace ClothPreview
{
    public partial class MainWindow : Window
    {
        List<MeshPart> _parts = new List<MeshPart>();
        BitmapSource _tex;
        ClothItem _item;
        readonly Dictionary<Gender, Dictionary<string, List<MeshPart>>> _bodies =
            new Dictionary<Gender, Dictionary<string, List<MeshPart>>>();
        bool _ready;

        public MainWindow()
        {
            InitializeComponent();
            _ready = true;
        }

        // ---------- Carga de archivos ----------

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Filter = "Ropa GTA V (*.ydd;*.ytd)|*.ydd;*.ytd" };
            if (dlg.ShowDialog() == true) LoadPaths(dlg.FileNames);
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] p) LoadPaths(p);
        }

        void LoadPaths(IEnumerable<string> paths)
        {
            var items = ClothPairing.Scan(paths);
            ItemList.ItemsSource = items;
            Info.Text = items.Count + " prendas encontradas";
            if (items.Count > 0) ItemList.SelectedIndex = 0;
        }

        void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _item = ItemList.SelectedItem as ClothItem;
            if (_item == null) return;

            try { _parts = RageMeshLoader.Load(_item.YddPath); }
            catch (Exception ex) { _parts = new List<MeshPart>(); Info.Text = "Error leyendo el modelo: " + ex.Message; }

            VariantBox.ItemsSource = _item.Textures;
            if (_item.Textures.Count > 0) VariantBox.SelectedIndex = 0;   // dispara el cambio de textura
            else { _tex = null; Rebuild(true); }
        }

        void VariantBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var v = VariantBox.SelectedItem as TextureVariant;
            try { _tex = v == null ? null : TextureLoader.Load(v.Path); }
            catch (Exception ex) { _tex = null; Info.Text = "Error leyendo la textura: " + ex.Message; }
            Rebuild(true);
        }

        void Option_Changed(object sender, RoutedEventArgs e) { if (_ready) Rebuild(false); }
        void Gender_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Rebuild(true); }

        // ---------- Cuerpo base ----------

        async void ImportBody_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Selecciona GTA5.exe dentro de tu carpeta de GTA V", Filter = "GTA5.exe|GTA5.exe" };
            if (dlg.ShowDialog() != true) return;
            string folder = Path.GetDirectoryName(dlg.FileName);

            ImportButton.IsEnabled = false;
            Info.Text = "Leyendo GTA V… puede tardar varios minutos. No cierres el programa.";
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                    BodyImporter.ImportFromGta(folder, s => Dispatcher.BeginInvoke(new Action(() => Info.Text = s))));
                _bodies.Clear();
                Rebuild(true);
            }
            catch (Exception ex) { Info.Text = "Error importando el cuerpo: " + ex.Message; }
            finally { ImportButton.IsEnabled = true; }
        }

        Dictionary<string, List<MeshPart>> GetBody(Gender g)
        {
            if (!_bodies.ContainsKey(g)) _bodies[g] = BodyImporter.LoadCached(g);
            return _bodies[g];
        }

        Gender ResolveGender(out string note)
        {
            switch (GenderBox.SelectedIndex)
            {
                case 1: note = "Hombre"; return Gender.Male;
                case 2: note = "Mujer"; return Gender.Female;
            }
            if (_item != null && _item.Gender == Gender.Female) { note = "Mujer (detectado por el nombre)"; return Gender.Female; }
            if (_item != null && _item.Gender == Gender.Male) { note = "Hombre (detectado por el nombre)"; return Gender.Male; }
            note = "Hombre (no se pudo detectar; cámbialo arriba si es de mujer)";
            return Gender.Male;
        }

        // ---------- Dibujo ----------

        static Material MakeMaterial(Brush brush, double glow)
        {
            // Difuso + un poco de brillo propio, para que no se vea oscuro ni apagado.
            var glowBrush = brush.Clone();
            glowBrush.Opacity = glow;
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            group.Children.Add(new EmissiveMaterial(glowBrush));
            return group;
        }

        static int AddPart(Model3DGroup group, MeshPart p, Material mat)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(p.Positions),
                TriangleIndices = new Int32Collection(p.Indices)
            };
            if (p.Normals != null) mesh.Normals = new Vector3DCollection(p.Normals);
            if (p.TexCoords != null) mesh.TextureCoordinates = new PointCollection(p.TexCoords);
            group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            return p.Indices.Length / 3;
        }

        void Rebuild(bool zoom)
        {
            if (!_ready) return;

            string genderNote;
            Gender gender = ResolveGender(out genderNote);

            Brush clothBrush = Brushes.LightGray;
            if (ShowTexture.IsChecked == true && _tex != null)
            {
                var ib = new ImageBrush(_tex) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 1, 1) };
                RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.Fant);
                clothBrush = ib;
            }
            Material clothMat = MakeMaterial(clothBrush, 0.45);

            var group = new Model3DGroup();
            string bodyNote = "";

            // Cuerpo (sin la pieza que reemplaza la prenda)
            if (ShowBody.IsChecked == true)
            {
                var body = GetBody(gender);
                if (body == null) bodyNote = "\nCuerpo: sin importar (pulsa el botón de abajo)";
                else
                {
                    Material skin = MakeMaterial(new SolidColorBrush(Color.FromRgb(214, 168, 140)), 0.35);
                    foreach (var kv in body)
                    {
                        if (_item != null && kv.Key == _item.Component) continue;
                        foreach (var part in kv.Value) AddPart(group, part, skin);
                    }
                }
            }

            // Prenda
            var wire = new Point3DCollection();
            int tris = 0;
            foreach (var p in _parts)
            {
                tris += AddPart(group, p, clothMat);
                if (WireBox.IsChecked == true)
                    for (int i = 0; i + 2 < p.Indices.Length; i += 3)
                    {
                        var a = p.Positions[p.Indices[i]];
                        var b = p.Positions[p.Indices[i + 1]];
                        var c = p.Positions[p.Indices[i + 2]];
                        wire.Add(a); wire.Add(b); wire.Add(b); wire.Add(c); wire.Add(c); wire.Add(a);
                    }
            }

            ModelHost.Content = group;
            Wire.Points = wire;
            if (zoom && group.Children.Count > 0) Viewport.ZoomExtents();

            string texInfo = _tex != null ? "Textura: " + _tex.PixelWidth + "x" + _tex.PixelHeight : "Sin textura";
            Info.Text = "Triángulos: " + tris.ToString("N0") + "\nPartes (geometrías): " + _parts.Count +
                        "\n" + texInfo + "\nPersonaje: " + genderNote + bodyNote;
        }
    }
}
