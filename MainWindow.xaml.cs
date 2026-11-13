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

        ClothItem _active;      // la prenda cuyos controles se muestran (la última elegida)
        bool _framed;
        readonly Dictionary<Gender, Dictionary<string, List<MeshPart>>> _bodies =
            new Dictionary<Gender, Dictionary<string, List<MeshPart>>>();
        readonly AxisAngleRotation3D _spin = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        bool _ready;
        bool _suppress;
        bool _suppressSel;
        readonly List<string> _paths = new List<string>();

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

        static bool BodyComplete(Gender g)
        {
            var b = BodyImporter.LoadCached(g);
            return b != null && b.Count >= 5;
        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (BodyComplete(Gender.Male) && BodyComplete(Gender.Female)) return;

            bool partial = File.Exists(BodyImporter.CachePath(Gender.Male)) || File.Exists(BodyImporter.CachePath(Gender.Female));
            string msg = partial
                ? "El cuerpo guardado está incompleto (faltan torso, piernas o manos).\nHay que volver a importarlo desde tu GTA V; tarda unos minutos.\n\n¿Importarlo ahora?"
                : "Para ver las prendas puestas en un personaje (hombre o mujer) hay que importar el cuerpo desde tu GTA V.\nSe hace una sola vez y tarda unos minutos.\n\n¿Importarlo ahora?";
            if (MessageBox.Show(msg, "420 Tool´s", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                ImportBody_Click(null, null);
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

        /// Añade archivos a la lista (sin quitar lo que ya había) y conserva las prendas equipadas.
        void LoadPaths(IEnumerable<string> paths)
        {
            foreach (var p in paths)
                if (!_paths.Contains(p, StringComparer.OrdinalIgnoreCase)) _paths.Add(p);

            var scanned = ClothPairing.Scan(_paths);
            var current = (ItemList.ItemsSource as List<ClothItem>) ?? new List<ClothItem>();
            var byPath = new Dictionary<string, ClothItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in current) byPath[c.YddPath] = c;

            var merged = new List<ClothItem>();
            var added = new List<ClothItem>();
            foreach (var sItem in scanned)
            {
                ClothItem ex;
                if (byPath.TryGetValue(sItem.YddPath, out ex))
                {
                    // Pueden haber llegado texturas nuevas para una prenda que ya estaba
                    if (ex.Textures.Count != sItem.Textures.Count)
                    {
                        ex.Textures.Clear();
                        ex.Textures.AddRange(sItem.Textures);
                        if (ex.Parts != null && ex.Tex == null && ex.Textures.Count > 0)
                            ex.Tex = SafeTexture(ex.Textures[0].Path);
                    }
                    merged.Add(ex);
                }
                else { merged.Add(sItem); added.Add(sItem); }
            }

            var selected = ItemList.SelectedItems.Cast<ClothItem>().Where(x => merged.Contains(x)).ToList();
            var activeBefore = _active;

            _suppressSel = true;
            try
            {
                ItemList.ItemsSource = merged;
                foreach (var x in selected) ItemList.SelectedItems.Add(x);
            }
            finally { _suppressSel = false; }

            _active = (activeBefore != null && selected.Contains(activeBefore)) ? activeBefore : selected.LastOrDefault();
            ShowActive();
            Rebuild(false);

            Info.Text = merged.Count + " prendas en la lista (" + added.Count + " nuevas).\nHaz clic en una para equiparla; puedes elegir varias.";

            // Si se añadió una sola prenda (o la lista estaba vacía), se equipa sola
            if (added.Count == 1) ItemList.SelectedItems.Add(added[0]);
            else if (current.Count == 0 && merged.Count > 0) ItemList.SelectedItems.Add(merged[0]);
        }

        // Quita las prendas equipadas (la lista se queda)
        void Clear_Click(object sender, RoutedEventArgs e) { ItemList.UnselectAll(); }

        // Vacía la lista y los archivos acumulados
        void Empty_Click(object sender, RoutedEventArgs e)
        {
            _paths.Clear();
            _active = null;
            _framed = false;
            _suppressSel = true;
            try { ItemList.ItemsSource = null; }
            finally { _suppressSel = false; }
            ShowActive();
            Rebuild(false);
            Info.Text = "Lista vacía. Arrastra tus archivos.";
        }

        static BitmapSource SafeTexture(string path)
        {
            try { return TextureLoader.Load(path); } catch { return null; }
        }

        void EnsureLoaded(ClothItem it)
        {
            if (it == null || it.Parts != null) return;
            try
            {
                it.Parts = RageMeshLoader.Load(it.YddPath);
                it.Diag = RageMeshLoader.LastDiag;
                it.Source = RageMeshLoader.LastSource;
            }
            catch (Exception ex)
            {
                it.Parts = new List<MeshPart>();
                it.Diag = "Error: " + ex.Message;
            }
            it.Toggles = it.Parts.Select((p, i) => new PartToggle
            {
                Name = "Parte " + (i + 1) + " · " + (p.Indices.Length / 3) + " tri",
                IsVisible = true
            }).ToList();
            it.VariantIndex = 0;
            if (it.Textures.Count > 0) it.Tex = SafeTexture(it.Textures[0].Path);
        }

        void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _suppressSel) return;
            foreach (var o in e.AddedItems) EnsureLoaded(o as ClothItem);

            var sel = ItemList.SelectedItems.Cast<ClothItem>().ToList();
            if (e.AddedItems.Count > 0) _active = e.AddedItems[e.AddedItems.Count - 1] as ClothItem;
            else if (_active == null || !sel.Contains(_active)) _active = sel.LastOrDefault();
            if (sel.Count == 0) _framed = false;

            ShowActive();
            Rebuild(false);
        }

        // Muestra en el panel los controles de la prenda activa
        void ShowActive()
        {
            _suppress = true;
            try
            {
                if (_active == null)
                {
                    VariantBox.ItemsSource = null;
                    PartList.ItemsSource = null;
                    PropPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    VariantBox.ItemsSource = _active.Textures;
                    VariantBox.SelectedIndex = _active.Textures.Count > 0 ? _active.VariantIndex : -1;
                    PartList.ItemsSource = _active.Toggles;
                    PropPanel.Visibility = _active.IsProp ? Visibility.Visible : Visibility.Collapsed;
                    OffXSlider.Value = _active.OffX;
                    OffYSlider.Value = _active.OffY;
                    OffZSlider.Value = _active.OffZ;
                }
            }
            finally { _suppress = false; }
        }

        void VariantBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _suppress || _active == null) return;
            int i = VariantBox.SelectedIndex;
            if (i < 0 || i >= _active.Textures.Count) return;
            _active.VariantIndex = i;
            _active.Tex = SafeTexture(_active.Textures[i].Path);
            Rebuild(false);
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

        void Prop_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready || _suppress || _active == null) return;
            _active.OffX = OffXSlider.Value;
            _active.OffY = OffYSlider.Value;
            _active.OffZ = OffZSlider.Value;
            Rebuild(false);
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
            string result = null;
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                    BodyImporter.ImportFromGta(folder, s => Dispatcher.BeginInvoke(new Action(() => Info.Text = s))));
                _bodies.Clear();
                Rebuild(true);
                result = Info.Text;
            }
            catch (Exception ex) { Info.Text = "Error importando el cuerpo:\n" + ex.Message; }
            finally { ImportButton.IsEnabled = true; }
        }

        Dictionary<string, List<MeshPart>> GetBody(Gender g)
        {
            if (!_bodies.ContainsKey(g)) _bodies[g] = BodyImporter.LoadCached(g);
            return _bodies[g];
        }

        Gender ResolveGender(IList<ClothItem> items, out string note)
        {
            if (GenMale.IsChecked == true) { note = "Hombre"; return Gender.Male; }
            if (GenFemale.IsChecked == true) { note = "Mujer"; return Gender.Female; }
            var f = items.Count(i => i.Gender == Gender.Female);
            var m = items.Count(i => i.Gender == Gender.Male);
            if (f > 0 && f >= m) { note = "Mujer (detectado)"; return Gender.Female; }
            if (m > 0) { note = "Hombre (detectado)"; return Gender.Male; }
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

        Material ClothMaterial(ClothItem it)
        {
            Brush brush = Brushes.LightGray;
            if (ShowTexture.IsChecked == true && it.Tex != null)
            {
                var ib = new ImageBrush(it.Tex) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 1, 1) };
                RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.Fant);
                brush = ib;
            }
            return MakeMaterial(brush, 0.45);
        }

        static int AddPart(Model3DGroup group, MeshPart p, Material mat, Vector3D offset)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(p.Positions),
                TriangleIndices = new Int32Collection(p.Indices)
            };
            if (p.Normals != null) mesh.Normals = new Vector3DCollection(p.Normals);
            if (p.TexCoords != null) mesh.TextureCoordinates = new PointCollection(p.TexCoords);
            var gm = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
            if (offset.X != 0 || offset.Y != 0 || offset.Z != 0) gm.Transform = new TranslateTransform3D(offset);
            group.Children.Add(gm);
            return p.Indices.Length / 3;
        }

        void Rebuild(bool zoom)
        {
            if (!_ready) return;

            var items = ItemList.SelectedItems.Cast<ClothItem>().ToList();
            string genderNote;
            Gender gender = ResolveGender(items, out genderNote);
            var body = GetBody(gender);

            var group = new Model3DGroup();
            string bodyNote = "";
            bool bodyMissing = false;

            // Cuerpo (sin las piezas que reemplazan las prendas elegidas)
            if (ShowBody.IsChecked == true)
            {
                if (body == null) { bodyMissing = true; bodyNote = "\nCUERPO    sin importar"; }
                else
                {
                    var hidden = new HashSet<string>(items.Select(i => i.Component));
                    Material skin = MakeMaterial(new SolidColorBrush(Color.FromRgb(214, 168, 140)), 0.35);
                    foreach (var kv in body)
                    {
                        if (hidden.Contains(kv.Key)) continue;
                        foreach (var part in kv.Value) AddPart(group, part, skin, new Vector3D());
                    }
                    bodyNote = "\nCUERPO    " + string.Join("·", body.Keys.OrderBy(k => k)) +
                               (body.Count < 5 ? "  (incompleto: vuelve a importar)" : "");
                }
            }

            // Prendas equipadas
            var wire = new Point3DCollection();
            int tris = 0, partsCount = 0;
            foreach (var it in items)
            {
                if (it.Parts == null) continue;
                var off = new Vector3D();
                if (it.IsProp) off = PropAnchor.For(it.Component, body) + new Vector3D(it.OffX, it.OffY, it.OffZ);
                Material mat = ClothMaterial(it);

                for (int n = 0; n < it.Parts.Count; n++)
                {
                    if (n < it.Toggles.Count && !it.Toggles[n].IsVisible) continue;
                    var p = it.Parts[n];
                    tris += AddPart(group, p, mat, off);
                    partsCount++;
                    if (WireBox.IsChecked == true)
                        for (int i = 0; i + 2 < p.Indices.Length; i += 3)
                        {
                            var a = p.Positions[p.Indices[i]] + off;
                            var b = p.Positions[p.Indices[i + 1]] + off;
                            var c = p.Positions[p.Indices[i + 2]] + off;
                            wire.Add(a); wire.Add(b); wire.Add(b); wire.Add(c); wire.Add(c); wire.Add(a);
                        }
                }
            }

            ModelHost.Content = group;
            Wire.Points = wire;
            if ((zoom || !_framed) && group.Children.Count > 0) { Viewport.ZoomExtents(); _framed = true; }

            BodyOverlay.Visibility = bodyMissing ? Visibility.Visible : Visibility.Collapsed;
            DropHint.Visibility = ItemList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemTitle.Text = items.Count == 0 ? "SIN PRENDA"
                           : items.Count == 1 ? items[0].Component.ToUpperInvariant() + "_" + items[0].Index.ToString("000")
                           : "OUTFIT · " + items.Count + " PRENDAS";

            string texInfo = _active != null && _active.Tex != null ? _active.Tex.PixelWidth + "x" + _active.Tex.PixelHeight : "ninguna";
            string text = "TRIÁNGULOS " + tris.ToString("N0") + "   PARTES " + partsCount + "   EQUIPADAS " + items.Count +
                          "\nTEXTURA   " + texInfo +
                          "\nPERSONAJE " + genderNote + bodyNote;
            if (_active != null)
            {
                if (!string.IsNullOrEmpty(_active.Source)) text += "\nMODELOS   " + _active.Source;
                if (!string.IsNullOrEmpty(_active.Diag)) text += "\nAVISO     " + _active.Diag;
            }
            Info.Text = text;
        }
    }
}
