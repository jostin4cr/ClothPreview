using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ClothPreview.Models;
using ClothPreview.Services;
using Microsoft.Win32;

namespace ClothPreview
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        List<MeshPart> _parts = new List<MeshPart>();
        List<PartToggle> _toggles = new List<PartToggle>();
        BitmapSource _tex;
        ClothItem _item;
        readonly Dictionary<Gender, Dictionary<string, List<MeshPart>>> _bodies =
            new Dictionary<Gender, Dictionary<string, List<MeshPart>>>();
        readonly AxisAngleRotation3D _spin = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        bool _ready;
        bool _suppress;

        public MainWindow()
        {
            InitializeComponent();
            var rot = new RotateTransform3D(_spin);
            ModelHost.Transform = rot;
            Wire.Transform = rot;
            _ready = true;
            Info.Text = "Listo.";
            Loaded += MainWindow_Loaded;
        }

        // Barra de título oscura (Windows 10/11)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4);
                DwmSetWindowAttribute(h, 19, ref on, 4);
                int cap = 0x000A0A0A; DwmSetWindowAttribute(h, 35, ref cap, 4);
                int txt = 0x00FFFFFF; DwmSetWindowAttribute(h, 36, ref txt, 4);
            }
            catch { }
        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            bool hasBody = File.Exists(BodyImporter.CachePath(Gender.Male)) && File.Exists(BodyImporter.CachePath(Gender.Female));
            if (!hasBody)
            {
                var r = MessageBox.Show(
                    "Para ver las prendas puestas en un personaje (hombre o mujer) hay que importar el cuerpo desde tu GTA V.\n" +
                    "Se hace una sola vez y tarda unos minutos.\n\n¿Importarlo ahora?",
                    "420 Tool´s", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes) ImportBody_Click(null, null);
            }
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

            _toggles = _parts.Select((p, i) => new PartToggle
            {
                Name = "Parte " + (i + 1) + " · " + (p.Indices.Length / 3) + " tri",
                IsVisible = true
            }).ToList();
            _suppress = true;
            PartList.ItemsSource = _toggles;
            _suppress = false;

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
        void Gender_Changed(object sender, RoutedEventArgs e) { if (_ready) Rebuild(true); }

        void Part_Toggled(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var t = cb == null ? null : cb.DataContext as PartToggle;
            if (t == null || _suppress) return;
            t.IsVisible = cb.IsChecked == true;
            if (_ready) Rebuild(false);
        }

        void Spin_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (SpinBox.IsChecked == true)
                _spin.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                _spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        }

        // ---------- Cuerpo base ----------

        async void ImportBody_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Selecciona GTA5.exe dentro de tu carpeta de GTA V", Filter = "GTA5.exe|GTA5.exe" };
            if (dlg.ShowDialog() != true) return;
            string folder = Path.GetDirectoryName(dlg.FileName);

            ImportButton.IsEnabled = false;
            Info.Text = "Leyendo GTA V… puede tardar varios minutos.\nNo cierres el programa.";
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                    BodyImporter.ImportFromGta(folder, s => Dispatcher.BeginInvoke(new Action(() => Info.Text = s))));
                _bodies.Clear();
                Rebuild(true);
            }
            catch (Exception ex) { Info.Text = "Error importando el cuerpo:\n" + ex.Message; }
            finally { ImportButton.IsEnabled = true; }
        }

        Dictionary<string, List<MeshPart>> GetBody(Gender g)
        {
            if (!_bodies.ContainsKey(g)) _bodies[g] = BodyImporter.LoadCached(g);
            return _bodies[g];
        }

        Gender ResolveGender(out string note)
        {
            if (GenMale.IsChecked == true) { note = "Hombre"; return Gender.Male; }
            if (GenFemale.IsChecked == true) { note = "Mujer"; return Gender.Female; }
            if (_item != null && _item.Gender == Gender.Female) { note = "Mujer (detectado)"; return Gender.Female; }
            if (_item != null && _item.Gender == Gender.Male) { note = "Hombre (detectado)"; return Gender.Male; }
            note = "Hombre (no detectado)";
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
            bool bodyMissing = false;

            // Cuerpo (sin la pieza que reemplaza la prenda)
            if (ShowBody.IsChecked == true)
            {
                var body = GetBody(gender);
                if (body == null) { bodyMissing = true; bodyNote = "\nCUERPO   sin importar"; }
                else
                {
                    Material skin = MakeMaterial(new SolidColorBrush(Color.FromRgb(214, 168, 140)), 0.35);
                    int shown = 0;
                    foreach (var kv in body)
                    {
                        if (_item != null && kv.Key == _item.Component) continue;
                        foreach (var part in kv.Value) { AddPart(group, part, skin); shown++; }
                    }
                    bodyNote = "\nCUERPO   " + body.Count + " piezas";
                }
            }

            // Prenda
            var wire = new Point3DCollection();
            int tris = 0;
            for (int n = 0; n < _parts.Count; n++)
            {
                if (n < _toggles.Count && !_toggles[n].IsVisible) continue;
                var p = _parts[n];
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

            BodyOverlay.Visibility = bodyMissing ? Visibility.Visible : Visibility.Collapsed;
            DropHint.Visibility = _item == null ? Visibility.Visible : Visibility.Collapsed;
            ItemTitle.Text = _item == null ? "SIN PRENDA" : _item.Component.ToUpperInvariant() + "_" + _item.Index.ToString("000");

            string texInfo = _tex != null ? _tex.PixelWidth + "x" + _tex.PixelHeight : "ninguna";
            Info.Text = "TRIÁNGULOS " + tris.ToString("N0") + "   PARTES " + _parts.Count +
                        "\nTEXTURA  " + texInfo +
                        "\nPERSONAJE " + genderNote + bodyNote +
                        "\nMODELOS  " + RageMeshLoader.LastSource;
        }
    }
}
