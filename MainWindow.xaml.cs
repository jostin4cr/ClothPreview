using System;
using System.Collections.Generic;
using System.Linq;
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
        bool _ready;

        public MainWindow()
        {
            InitializeComponent();
            _ready = true;
        }

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
            var item = ItemList.SelectedItem as ClothItem;
            if (item == null) return;

            try { _parts = RageMeshLoader.Load(item.YddPath); }
            catch (Exception ex) { _parts = new List<MeshPart>(); Info.Text = "Error leyendo el modelo: " + ex.Message; }

            VariantBox.ItemsSource = item.Textures;
            if (item.Textures.Count > 0) VariantBox.SelectedIndex = 0;   // dispara el cambio de textura
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

        void Rebuild(bool zoom)
        {
            if (!_ready) return;

            Material mat = (ShowTexture.IsChecked == true && _tex != null)
                ? new DiffuseMaterial(new ImageBrush(_tex) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 1, 1) })
                : new DiffuseMaterial(Brushes.LightGray);

            var group = new Model3DGroup();
            var wire = new Point3DCollection();
            int tris = 0;

            foreach (var p in _parts)
            {
                var mesh = new MeshGeometry3D
                {
                    Positions = new Point3DCollection(p.Positions),
                    TriangleIndices = new Int32Collection(p.Indices)
                };
                if (p.Normals != null) mesh.Normals = new Vector3DCollection(p.Normals);
                if (p.TexCoords != null) mesh.TextureCoordinates = new PointCollection(p.TexCoords);
                group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
                tris += p.Indices.Length / 3;

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
            if (zoom && _parts.Count > 0) Viewport.ZoomExtents();

            string texInfo = _tex != null ? $"Textura: {_tex.PixelWidth}x{_tex.PixelHeight}" : "Sin textura";
            Info.Text = $"Triángulos: {tris:N0}\nPartes (geometrías): {_parts.Count}\n{texInfo}";
        }
    }
}
