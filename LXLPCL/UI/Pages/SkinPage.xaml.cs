using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class SkinPage : PageBase
{
    private const int Size = 64;
    private const double Pixel = 9;
    private const int TexScale = 16;
    private const int MaxUndo = 30;

    private static readonly (string Part, string Name, int X, int Y, int W, int H)[] Regions =
    {
        ("head", "头", 0, 0, 32, 16), ("head", "帽子", 32, 0, 32, 16),
        ("body", "身体", 16, 16, 24, 16), ("body", "外套", 16, 32, 24, 16),
        ("rightArm", "右臂", 40, 16, 16, 16), ("rightArm", "右袖", 40, 32, 16, 16),
        ("rightLeg", "右腿", 0, 16, 16, 16), ("rightLeg", "右裤", 0, 32, 16, 16),
        ("leftLeg", "左腿", 16, 48, 16, 16), ("leftLeg", "左裤", 0, 48, 16, 16),
        ("leftArm", "左臂", 32, 48, 16, 16), ("leftArm", "左袖", 48, 48, 16, 16)
    };

    private readonly byte[] _base = new byte[Size * Size * 4];
    private readonly byte[] _pixels = new byte[Size * Size * 4];
    private readonly List<SkinLayer> _layers = new();
    private SkinLayer? _active;

    private readonly bool[] _selection = new bool[Size * Size];
    private bool _selectionActive;
    private WriteableBitmap _selectionBitmap = null!;

    private readonly List<DocState> _undo = new();
    private readonly List<DocState> _redo = new();

    private WriteableBitmap _bitmap = null!;
    private Color _color = Color.FromRgb(0xE8, 0xB4, 0x8F);
    private string _tool = "brush";
    private int _brushSize = 1;
    private int _tolerance;
    private bool _drawing;
    private int _lastX = -1;
    private int _lastY = -1;
    private bool _ready;
    private double _pixelSize = Pixel;
    private bool _refreshUi;

    // 图章
    private (int X, int Y)? _stampSource;
    private int _stampAnchorX, _stampAnchorY;
    private bool _stampAnchored;

    // 套索
    private readonly List<Point> _lassoPoints = new();
    private Polyline? _lassoLine;
    private bool _lassoing;

    // 调色器
    private readonly List<string> _recent = new();
    private double _hue, _sat, _val, _alpha = 1;
    private bool _draggingSv, _draggingHue, _draggingAlpha;

    // 3D 预览
    private readonly List<FaceTexture> _faces = new();
    private readonly Dictionary<MeshGeometry3D, FaceTexture> _meshToFace = new();
    private readonly FaceTexture?[] _pixelFace = new FaceTexture?[Size * Size];
    private readonly ScaleTransform3D _scale = new(1, 1, 1, 0, 16, 0);
    private Model3DGroup? _modelGroup;
    private ModelVisual3D? _floorVisual;
    private Model3DGroup? _floorModel;

    // 行走模式
    private readonly TranslateTransform3D _modelOffset = new(0, 0, 0);
    private readonly TranslateTransform3D _floorShift = new(0, 0, 0);
    private readonly Dictionary<string, AxisAngleRotation3D> _limbRotations = new();
    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Model3DGroup? _currentPartGroup;
    private bool _walkMode;
    private double _walkPhase;
    private double _posX, _posZ, _jumpY, _jumpVelocity;
    private double _facingAngle, _camAngle;
    private PerspectiveCamera? _camera;
    private readonly RotateTransform3D _yaw = new(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -28), 0, 16, 0);
    private readonly RotateTransform3D _pitch = new(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), 0, 16, 0);
    private DispatcherTimer? _previewTimer;
    private bool _previewBuilt;
    private bool _rotating;
    private bool _painting3D;
    private Point _rotateStart;
    private double _yawAngle = -28;
    private double _pitchAngle;
    private double _zoom = 1;
    private string _selectedPart = "head";
    private string? _selectedRegion;
    private (int X, int Y)? _hoverPixel;

    public SkinPage()
    {
        InitializeComponent();
        _bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        _selectionBitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        SkinImage.Source = _bitmap;
        SelectionImage.Source = _selectionBitmap;
        _ready = true;

        BrushSizeCombo.ItemsSource = new[] { "1 px", "2 px", "3 px", "4 px", "6 px" };
        BrushSizeCombo.SelectedIndex = 0;

        var (h, s, v) = ColorToHsv(_color);
        _hue = h;
        _sat = s;
        _val = v;
        _alpha = 1;
        SeedRecent();
        UpdateColorDisplay();
        UpdatePickerVisuals();

        BuildPreview();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreviewTextures();
        };
        _walkTimer.Tick += Walk_Tick;

        ApplySampleTemplate(_base);
        Recompose();
        Flush();
        RefreshPreviewTextures();
        RebuildOverlay();
        RefreshLayerList();
        BuildPartRegions();
        ApplyViewMode("3d");
        RefreshLibrary();

        SvPanel.SizeChanged += (_, _) => UpdatePickerVisuals();
        HueTrack.SizeChanged += (_, _) => UpdatePickerVisuals();
        AlphaTrack.SizeChanged += (_, _) => UpdatePickerVisuals();
        Loaded += (_, _) =>
        {
            RebuildOverlay();
            UpdatePickerVisuals();
            if (Window.GetWindow(this) is { } window)
            {
                window.PreviewKeyDown -= Window_PreviewKeyDown;
                window.PreviewKeyDown += Window_PreviewKeyDown;
            }
        };
        Unloaded += (_, _) =>
        {
            _walkTimer.Stop();
            if (Window.GetWindow(this) is { } window)
                window.PreviewKeyDown -= Window_PreviewKeyDown;
        };
    }

    /// <summary>「我的皮肤」页面点击编辑时，把要载入的文件路径放在这里。</summary>
    public static string? PendingSkinPath;

    public override void OnShown()
    {
        RefreshLibrary();
        if (PendingSkinPath is not { } pending) return;
        PendingSkinPath = null;
        LoadSkinFile(pending);
    }

    /// <summary>把皮肤库里的整张皮肤载入画布（会清空现有图层）。</summary>
    private void LoadSkinFile(string path)
    {
        try
        {
            var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth != Size || frame.PixelHeight != Size)
            {
                MessageDialog.Show(Window.GetWindow(this), "无法载入", "这张皮肤的尺寸不是 64×64。", warning: true);
                return;
            }

            var source = new byte[Size * Size * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(source, Size * 4, 0);
            PushUndo();
            Buffer.BlockCopy(source, 0, _base, 0, _base.Length);
            _layers.Clear();
            _active = null;
            Recompose();
            Flush();
            RefreshLayerList();
            SkinSubtitleText.Text = "已载入皮肤：" + Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "载入失败", ex.Message, warning: true);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || e.Handled) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        if (_walkMode)
        {
            if (e.Key == Key.Escape)
            {
                SetWalkMode(false);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                _camAngle = _camAngle switch
                {
                    0 => 180,
                    180 => 90,
                    90 => -90,
                    _ => 0
                };
                UpdateWalkCamera();
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(true); e.Handled = true; return;
                case Key.Y: Redo(); e.Handled = true; return;
                case Key.S: SaveSkin_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                case Key.A:
                    SelectAll();
                    e.Handled = true;
                    return;
                case Key.D:
                    ClearSelection();
                    e.Handled = true;
                    return;
            }
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            Redo();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var tool = e.Key switch
        {
            Key.B => "brush",
            Key.E => "eraser",
            Key.I => "picker",
            Key.G => "fill",
            Key.C => "stamp",
            Key.M => "wand",
            Key.L => "lasso",
            _ => null
        };
        if (tool == null) return;
        SelectTool(tool);
        e.Handled = true;
    }

    private void SelectTool(string tool)
    {
        _tool = tool;
        var radio = tool switch
        {
            "eraser" => EraserRadio,
            "picker" => PickerRadio,
            "fill" => FillRadio,
            "stamp" => StampRadio,
            "wand" => WandRadio,
            "lasso" => LassoRadio,
            _ => BrushRadio
        };
        if (radio.IsChecked != true) radio.IsChecked = true;
    }

    // ---------- 文档与图层 ----------

    private DocState CaptureDoc()
    {
        var state = new DocState
        {
            Base = (byte[])_base.Clone(),
            ActiveIndex = _active == null ? -1 : _layers.IndexOf(_active)
        };
        foreach (var layer in _layers) state.Layers.Add(layer.Clone());
        return state;
    }

    private void RestoreDoc(DocState state)
    {
        Buffer.BlockCopy(state.Base, 0, _base, 0, _base.Length);
        _layers.Clear();
        foreach (var layer in state.Layers) _layers.Add(layer.Clone());
        _active = state.ActiveIndex >= 0 && state.ActiveIndex < _layers.Count
            ? _layers[state.ActiveIndex]
            : null;
        Recompose();
        Flush();
        RefreshLayerList();
        RebuildOverlay();
    }

    private void PushUndo()
    {
        _undo.Add(CaptureDoc());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private SkinLayer EnsureLayer()
    {
        if (_active != null) return _active;
        var layer = new SkinLayer { Name = "图层 " + (_layers.Count + 1) };
        _layers.Add(layer);
        _active = layer;
        RefreshLayerList();
        return layer;
    }

    private byte[] EditBuffer => _active?.Pixels ?? _base;

    private void Recompose()
    {
        Buffer.BlockCopy(_base, 0, _pixels, 0, _pixels.Length);
        foreach (var layer in _layers)
        {
            if (!layer.Visible || layer.Opacity <= 0) continue;
            var layerAlpha = layer.Opacity / 100.0;
            for (var i = 0; i < _pixels.Length; i += 4)
            {
                var sa = layer.Pixels[i + 3];
                if (sa == 0) continue;
                var a = sa / 255.0 * layerAlpha;
                if (a <= 0) continue;
                BlendPixel(i, layer.Pixels[i + 2], layer.Pixels[i + 1], layer.Pixels[i], a, layer.Blend);
            }
        }
    }

    private void BlendPixel(int index, byte r, byte g, byte b, double a, int mode)
    {
        var dr = _pixels[index + 2] / 255.0;
        var dg = _pixels[index + 1] / 255.0;
        var db = _pixels[index] / 255.0;
        var sr = r / 255.0;
        var sg = g / 255.0;
        var sb = b / 255.0;

        double br = sr, bg = sg, bb = sb;
        switch (mode)
        {
            case 1: // 正片叠底
                br = dr * sr; bg = dg * sg; bb = db * sb;
                break;
            case 2: // 滤色
                br = 1 - (1 - dr) * (1 - sr);
                bg = 1 - (1 - dg) * (1 - sg);
                bb = 1 - (1 - db) * (1 - sb);
                break;
            case 3: // 叠加
                br = dr < 0.5 ? 2 * dr * sr : 1 - 2 * (1 - dr) * (1 - sr);
                bg = dg < 0.5 ? 2 * dg * sg : 1 - 2 * (1 - dg) * (1 - sg);
                bb = db < 0.5 ? 2 * db * sb : 1 - 2 * (1 - db) * (1 - sb);
                break;
            case 4: // 变暗
                br = Math.Min(dr, sr); bg = Math.Min(dg, sg); bb = Math.Min(db, sb);
                break;
            case 5: // 变亮
                br = Math.Max(dr, sr); bg = Math.Max(dg, sg); bb = Math.Max(db, sb);
                break;
        }

        var da = _pixels[index + 3] / 255.0;
        var outA = a + da * (1 - a);
        if (outA <= 0)
        {
            _pixels[index] = _pixels[index + 1] = _pixels[index + 2] = _pixels[index + 3] = 0;
            return;
        }
        _pixels[index + 2] = (byte)Math.Round(Math.Clamp((br * a + dr * da * (1 - a)) / outA, 0, 1) * 255);
        _pixels[index + 1] = (byte)Math.Round(Math.Clamp((bg * a + dg * da * (1 - a)) / outA, 0, 1) * 255);
        _pixels[index] = (byte)Math.Round(Math.Clamp((bb * a + db * da * (1 - a)) / outA, 0, 1) * 255);
        _pixels[index + 3] = (byte)Math.Round(outA * 255);
    }

    private void RefreshLayerList()
    {
        _refreshUi = true;
        LayerPanel.Children.Clear();
        // 列表从上到下 = 从最上层到最底层，基础皮肤固定在底部且锁定
        for (var i = _layers.Count - 1; i >= 0; i--)
            LayerPanel.Children.Add(BuildLayerRow(_layers[i], _layers[i].Name));
        LayerPanel.Children.Add(BuildLayerRow(null, "基础皮肤"));

        LayerSummaryText.Text = $"{_layers.Count + 1} 层";
        BlendCombo.SelectedIndex = _active?.Blend ?? 0;
        LayerOpacitySlider.Value = _active?.Opacity ?? 100;
        LayerOpacityText.Text = (_active?.Opacity ?? 100) + "%";
        BlendCombo.IsEnabled = _active != null;
        LayerOpacitySlider.IsEnabled = _active != null;
        _refreshUi = false;
    }

    private Border BuildLayerRow(SkinLayer? layer, string name)
    {
        var selected = layer != null ? ReferenceEquals(layer, _active) : false;
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 4),
            BorderThickness = new Thickness(1),
            Background = selected
                ? (Brush)FindResource("AccentLightBrush")
                : (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = selected
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrush"),
            Cursor = layer == null ? Cursors.Arrow : Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var eye = new CheckBox
        {
            IsChecked = layer?.Visible ?? true,
            IsEnabled = layer != null,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "显示 / 隐藏"
        };
        if (layer != null)
        {
            eye.Checked += (_, _) => SetLayerVisible(layer, true);
            eye.Unchecked += (_, _) => SetLayerVisible(layer, false);
        }
        Grid.SetColumn(eye, 0);
        grid.Children.Add(eye);

        if (layer == null)
        {
            var label = new TextBlock
            {
                Text = name,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("SurfaceBrush"),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                Child = new TextBlock
                {
                    Text = "锁定",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextTertiaryBrush")
                }
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        else
        {
            var nameBox = new TextBox
            {
                Text = name,
                FontSize = 11,
                Height = 24,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };
            nameBox.TextChanged += (_, _) => layer.Name = nameBox.Text;
            Grid.SetColumn(nameBox, 1);
            grid.Children.Add(nameBox);

            var delete = new Button
            {
                Content = "✕",
                FontSize = 10,
                Width = 20,
                Height = 20,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                ToolTip = "删除图层"
            };
            delete.Click += (_, _) => DeleteLayer(layer);
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            border.MouseLeftButtonDown += (_, _) => SelectLayer(layer);
        }

        border.Child = grid;
        return border;
    }

    private void SetLayerVisible(SkinLayer layer, bool visible)
    {
        layer.Visible = visible;
        Recompose();
        Flush();
    }

    private void SelectLayer(SkinLayer layer)
    {
        _active = layer;
        RefreshLayerList();
        RebuildOverlay();
    }

    private void DeleteLayer(SkinLayer layer)
    {
        PushUndo();
        var index = _layers.IndexOf(layer);
        if (index >= 0) _layers.RemoveAt(index);
        if (ReferenceEquals(_active, layer))
            _active = _layers.Count > 0 ? _layers[Math.Min(index, _layers.Count - 1)] : null;
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        var layer = new SkinLayer { Name = "图层 " + (_layers.Count + 1) };
        _layers.Add(layer);
        _active = layer;
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void DuplicateLayer_Click(object sender, RoutedEventArgs e)
    {
        var source = EnsureLayer();
        PushUndo();
        var clone = source.Clone();
        clone.Name = source.Name + " 副本";
        _layers.Insert(_layers.IndexOf(source) + 1, clone);
        _active = clone;
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void MergeLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        PushUndo();
        var index = _layers.IndexOf(_active);
        var layer = _active;
        if (index > 0)
        {
            var below = _layers[index - 1];
            for (var i = 0; i < below.Pixels.Length; i += 4)
            {
                var a = layer.Pixels[i + 3] / 255.0 * layer.Opacity / 100.0;
                if (a <= 0) continue;
                below.Pixels[i] = layer.Pixels[i];
                below.Pixels[i + 1] = layer.Pixels[i + 1];
                below.Pixels[i + 2] = layer.Pixels[i + 2];
                below.Pixels[i + 3] = (byte)Math.Round(Math.Clamp(
                    (layer.Pixels[i + 3] / 255.0 * layer.Opacity / 100.0 +
                     below.Pixels[i + 3] / 255.0 * (1 - a)), 0, 1) * 255);
            }
            _layers.RemoveAt(index);
            _active = below;
        }
        else
        {
            for (var i = 0; i < layer.Pixels.Length; i += 4)
            {
                if (layer.Pixels[i + 3] == 0) continue;
                _base[i] = layer.Pixels[i];
                _base[i + 1] = layer.Pixels[i + 1];
                _base[i + 2] = layer.Pixels[i + 2];
                _base[i + 3] = layer.Pixels[i + 3];
            }
            _layers.RemoveAt(index);
            _active = _layers.Count > 0 ? _layers[^1] : null;
        }
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void MoveLayerUp_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        var index = _layers.IndexOf(_active);
        if (index < 0 || index >= _layers.Count - 1) return;
        PushUndo();
        _layers.RemoveAt(index);
        _layers.Insert(index + 1, _active);
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void MoveLayerDown_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        var index = _layers.IndexOf(_active);
        if (index <= 0) return;
        PushUndo();
        _layers.RemoveAt(index);
        _layers.Insert(index - 1, _active);
        Recompose();
        Flush();
        RefreshLayerList();
    }

    private void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_active != null) DeleteLayer(_active);
    }

    private void Blend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshUi || _active == null) return;
        _active.Blend = Math.Max(0, BlendCombo.SelectedIndex);
        Recompose();
        Flush();
    }

    private void LayerOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_active == null) return;
        _active.Opacity = (int)Math.Round(e.NewValue);
        LayerOpacityText.Text = _active.Opacity + "%";
        if (_refreshUi) return;
        Recompose();
        Flush();
    }

    // ---------- 选区 ----------

    private void UpdateSelectionOverlay()
    {
        var bytes = new byte[Size * Size * 4];
        if (_selectionActive)
        {
            for (var i = 0; i < _selection.Length; i++)
            {
                if (!_selection[i]) continue;
                bytes[i * 4] = 0xFF;
                bytes[i * 4 + 1] = 0x00;
                bytes[i * 4 + 2] = 0xFF;
                bytes[i * 4 + 3] = 0x50;
            }
        }
        _selectionBitmap.WritePixels(new Int32Rect(0, 0, Size, Size), bytes, Size * 4, 0);
        var count = _selection.Count(v => v);
        SelectionPanel.Visibility = _selectionActive ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = $"选区：{count} 像素";
    }

    private void SelectAll()
    {
        Array.Fill(_selection, true);
        _selectionActive = true;
        SelectionPanel.Visibility = Visibility.Visible;
        SelectionText.Text = $"选区：{Size * Size} 像素";
        UpdateSelectionOverlay();
        RebuildOverlay();
    }

    private void ClearSelection()
    {
        Array.Clear(_selection);
        _selectionActive = false;
        SelectionPanel.Visibility = Visibility.Collapsed;
        SelectionText.Text = "选区：0 像素";
        UpdateSelectionOverlay();
        RebuildOverlay();
    }

    private void SelectionAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (action == "clear")
        {
            ClearSelection();
            return;
        }
        if (!_selectionActive) return;
        PushUndo();
        var layer = EnsureLayer();
        if (action == "fill" || action == "erase")
        {
            var color = action == "erase" ? Colors.Transparent : _color;
            for (var i = 0; i < _selection.Length; i++)
            {
                if (!_selection[i]) continue;
                WritePixel(layer.Pixels, i * 4, color);
            }
        }
        else if (action == "outline")
        {
            var snapshot = (bool[])_selection.Clone();
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (!snapshot[y * Size + x]) continue;
                    if (x > 0 && snapshot[y * Size + x - 1] &&
                        x < Size - 1 && snapshot[y * Size + x + 1] &&
                        y > 0 && snapshot[(y - 1) * Size + x] &&
                        y < Size - 1 && snapshot[(y + 1) * Size + x]) continue;
                    if (x > 0) WritePixel(layer.Pixels, (y * Size + x - 1) * 4, _color);
                    if (x < Size - 1) WritePixel(layer.Pixels, (y * Size + x + 1) * 4, _color);
                    if (y > 0) WritePixel(layer.Pixels, ((y - 1) * Size + x) * 4, _color);
                    if (y < Size - 1) WritePixel(layer.Pixels, ((y + 1) * Size + x) * 4, _color);
                }
            }
        }
        Recompose();
        Flush();
    }

    // ---------- 初始化 ----------

    private void SeedRecent()
    {
        _recent.AddRange(new[] { "#E8B48F", "#C98C63", "#3B2A1E", "#1C1C1C", "#2E8FC8", "#FFFFFF" });
        RebuildRecent();
    }

    private void RebuildRecent()
    {
        RecentPanel.Children.Clear();
        foreach (var hex in _recent)
        {
            var chip = new Button
            {
                Style = (Style)FindResource("ColorChip"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = hex
            };
            chip.Click += Recent_Click;
            RecentPanel.Children.Add(chip);
        }
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        SetColorFromExternal(color);
        if (EraserRadio.IsChecked == true) BrushRadio.IsChecked = true;
    }

    private void AddRecent(Color color)
    {
        if (color.A == 0) return;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _recent.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, hex);
        while (_recent.Count > 8) _recent.RemoveAt(_recent.Count - 1);
        RebuildRecent();
    }

    private void SetColorFromExternal(Color color)
    {
        var (h, s, v) = ColorToHsv(color);
        _hue = h;
        _sat = s;
        _val = v;
        _alpha = color.A / 255.0;
        _color = color;
        UpdateColorDisplay();
        UpdatePickerVisuals();
        AddRecent(color);
    }

    private void UpdatePickerVisuals()
    {
        SvHueLayer.Background = new SolidColorBrush(HsvToColor(_hue, 1, 1, 1));

        var svWidth = SvPanel.ActualWidth;
        var svHeight = SvPanel.ActualHeight;
        Canvas.SetLeft(SvThumb, Math.Clamp(_sat * svWidth - 6, 0, Math.Max(svWidth - 12, 0)));
        Canvas.SetTop(SvThumb, Math.Clamp((1 - _val) * svHeight - 6, 0, Math.Max(svHeight - 12, 0)));

        var hueWidth = HueTrack.ActualWidth;
        Canvas.SetLeft(HueThumb, Math.Clamp(_hue / 360.0 * hueWidth - 3, 0, Math.Max(hueWidth - 6, 0)));
        Canvas.SetTop(HueThumb, 0);

        var alphaWidth = AlphaTrack.ActualWidth;
        Canvas.SetLeft(AlphaThumb, Math.Clamp(_alpha * alphaWidth - 3, 0, Math.Max(alphaWidth - 6, 0)));
        Canvas.SetTop(AlphaThumb, 0);

        var solid = HsvToColor(_hue, _sat, _val, 1);
        AlphaLayer.Background = new LinearGradientBrush(
            Color.FromArgb(0, solid.R, solid.G, solid.B), solid,
            new Point(0, 0.5), new Point(1, 0.5));
    }

    private void CommitPickerColor()
    {
        if (!_ready) return;
        _color = HsvToColor(_hue, _sat, _val, _alpha);
        UpdateColorDisplay();
        UpdatePickerVisuals();
    }

    private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvPanel.CaptureMouse();
        PickSv(e);
    }

    private void Sv_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSv) PickSv(e);
    }

    private void Sv_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingSv) return;
        _draggingSv = false;
        SvPanel.ReleaseMouseCapture();
        AddRecent(_color);
    }

    private void PickSv(MouseEventArgs e)
    {
        var position = e.GetPosition(SvPanel);
        _sat = Math.Clamp(position.X / Math.Max(SvPanel.ActualWidth, 1), 0, 1);
        _val = Math.Clamp(1 - position.Y / Math.Max(SvPanel.ActualHeight, 1), 0, 1);
        CommitPickerColor();
    }

    private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueTrack.CaptureMouse();
        PickHue(e);
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue) PickHue(e);
    }

    private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingHue) return;
        _draggingHue = false;
        HueTrack.ReleaseMouseCapture();
        AddRecent(_color);
    }

    private void PickHue(MouseEventArgs e)
    {
        var position = e.GetPosition(HueTrack);
        _hue = Math.Clamp(position.X / Math.Max(HueTrack.ActualWidth, 1), 0, 1) * 360;
        CommitPickerColor();
    }

    private void Alpha_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingAlpha = true;
        AlphaTrack.CaptureMouse();
        PickAlpha(e);
    }

    private void Alpha_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingAlpha) PickAlpha(e);
    }

    private void Alpha_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingAlpha) return;
        _draggingAlpha = false;
        AlphaTrack.ReleaseMouseCapture();
        AddRecent(_color);
    }

    private void PickAlpha(MouseEventArgs e)
    {
        var position = e.GetPosition(AlphaTrack);
        _alpha = Math.Clamp(position.X / Math.Max(AlphaTrack.ActualWidth, 1), 0, 1);
        CommitPickerColor();
    }

    private static (double H, double S, double V) ColorToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;
        var sat = max <= 0 ? 0 : delta / max;
        return (hue, sat, max);
    }

    private static Color HsvToColor(double h, double s, double v, double a)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(
            (byte)Math.Round(Math.Clamp(a, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(r + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(g + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(b + m, 0, 1) * 255));
    }

    // ---------- 画布 ----------

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeCanvas();

    /// <summary>按宿主实际大小取整数倍缩放，避免 2D 画布超出容器被裁切。</summary>
    private void ResizeCanvas()
    {
        if (!View2DHost.IsVisible) return;
        var available = Math.Min(View2DHost.ActualWidth - 12, View2DHost.ActualHeight - 12);
        if (available < Size) return;
        var scale = Math.Floor(available / Size);
        if (scale < 1) scale = 1;
        _pixelSize = scale;

        var side = Size * _pixelSize;
        SkinCanvas.Width = side;
        SkinCanvas.Height = side;
        SkinImage.Width = side;
        SkinImage.Height = side;
        SelectionImage.Width = side;
        SelectionImage.Height = side;
        GridOverlay.Width = side;
        GridOverlay.Height = side;
        RebuildOverlay();
    }

    private void RebuildOverlay()
    {
        GridOverlay.Children.Clear();
        if (GridOverlay.ActualWidth <= 0) return;

        if (GridToggle.IsChecked == true)
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));
            brush.Freeze();
            for (var i = 0; i <= Size; i++)
            {
                var position = i * _pixelSize;
                var vertical = new Rectangle { Width = 1, Height = Size * _pixelSize, Fill = brush };
                Canvas.SetLeft(vertical, position);
                Canvas.SetTop(vertical, 0);
                GridOverlay.Children.Add(vertical);

                var horizontal = new Rectangle { Width = Size * _pixelSize, Height = 1, Fill = brush };
                Canvas.SetLeft(horizontal, 0);
                Canvas.SetTop(horizontal, position);
                GridOverlay.Children.Add(horizontal);
            }
        }

        if (UvToggle.IsChecked == true)
        {
            for (var i = 0; i < Regions.Length; i++)
            {
                var region = Regions[i];
                var fill = new SolidColorBrush(Color.FromArgb(0x2E, (byte)(60 + i * 14 % 160), (byte)(150 - i * 9 % 90), 0xE0));
                fill.Freeze();
                var box = new Rectangle
                {
                    Width = region.W * _pixelSize,
                    Height = region.H * _pixelSize,
                    Fill = fill
                };
                Canvas.SetLeft(box, region.X * _pixelSize);
                Canvas.SetTop(box, region.Y * _pixelSize);
                GridOverlay.Children.Add(box);

                if (region.W * _pixelSize >= 54)
                {
                    var label = new TextBlock
                    {
                        Text = region.Name,
                        FontSize = 10,
                        Foreground = Brushes.White,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(label, region.X * _pixelSize + 4);
                    Canvas.SetTop(label, region.Y * _pixelSize + 3);
                    GridOverlay.Children.Add(label);
                }
            }
        }

        if (PartToggle.IsChecked == true)
        {
            var stroke = new SolidColorBrush(Color.FromArgb(0x8C, 0x34, 0xD3, 0x99));
            stroke.Freeze();
            foreach (var region in Regions)
                AddRegionRect(region.X, region.Y, region.W, region.H, stroke, 1);
        }

        foreach (var region in Regions.Where(r => r.Part == _selectedPart))
        {
            var isActive = _selectedRegion == null || region.Name == _selectedRegion;
            if (!isActive) continue;
            var stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0xF5, 0x9E, 0x0B));
            stroke.Freeze();
            AddRegionRect(region.X, region.Y, region.W, region.H, stroke, 2);
        }

        if (_hoverPixel is { } hover)
        {
            var marker = new Rectangle
            {
                Width = _pixelSize,
                Height = _pixelSize,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(marker, hover.X * _pixelSize);
            Canvas.SetTop(marker, hover.Y * _pixelSize);
            GridOverlay.Children.Add(marker);
        }
    }

    private void AddRegionRect(int x, int y, int w, int h, Brush stroke, double thickness)
    {
        var rect = new Rectangle
        {
            Width = w * _pixelSize - 1,
            Height = h * _pixelSize - 1,
            Stroke = stroke,
            StrokeThickness = thickness
        };
        Canvas.SetLeft(rect, x * _pixelSize);
        Canvas.SetTop(rect, y * _pixelSize);
        GridOverlay.Children.Add(rect);
    }

    private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RebuildOverlay();
        if (ReferenceEquals(sender, GridToggle))
        {
            if (_floorVisual != null)
                _floorVisual.Content = GridToggle.IsChecked == true ? _floorModel : null;
            RefreshPreviewTextures();
        }
    }

    private (int X, int Y)? PointToPixel(MouseEventArgs e)
    {
        var position = e.GetPosition(SkinImage);
        var width = SkinImage.ActualWidth;
        if (width <= 0) return null;
        var x = (int)Math.Floor(position.X / width * Size);
        var y = (int)Math.Floor(position.Y / width * Size);
        if (x < 0 || y < 0 || x >= Size || y >= Size) return null;
        return (x, y);
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = PointToPixel(e);
        if (point == null) return;
        var x = point.Value.X;
        var y = point.Value.Y;

        if (_tool == "picker")
        {
            var picked = GetCompositePixel(x, y);
            if (picked.A > 0)
            {
                SetColorFromExternal(picked);
                SelectTool("brush");
            }
            return;
        }

        if (_tool == "wand")
        {
            SelectByWand(x, y);
            return;
        }

        if (_tool == "lasso")
        {
            _lassoing = true;
            _lassoPoints.Clear();
            _lassoPoints.Add(new Point(x + 0.5, y + 0.5));
            _lassoLine = new Polyline
            {
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            GridOverlay.Children.Add(_lassoLine);
            SkinImage.CaptureMouse();
            return;
        }

        if (_tool == "stamp" && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _stampSource = (x, y);
            _stampAnchored = false;
            CursorText.Text = $"取样 {x},{y}";
            return;
        }

        PushUndo();
        EnsureLayer();
        if (_tool == "fill")
        {
            FloodFill(x, y, GetCompositePixel(x, y), _active!.Pixels);
            Recompose();
            Flush();
            return;
        }

        _drawing = true;
        _lastX = x;
        _lastY = y;
        if (_tool == "stamp") _stampAnchored = false;
        Paint(x, y);
        SkinImage.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var point = PointToPixel(e);
        CursorText.Text = point == null ? "—" : $"{point.Value.X}, {point.Value.Y}";
        if (_lassoing)
        {
            if (point != null)
            {
                var next = new Point(point.Value.X + 0.5, point.Value.Y + 0.5);
                var last = _lassoPoints[^1];
                if (Math.Abs(next.X - last.X) > 0 || Math.Abs(next.Y - last.Y) > 0)
                    _lassoPoints.Add(next);
                if (_lassoLine != null)
                {
                    _lassoLine.Points.Clear();
                    foreach (var p in _lassoPoints)
                        _lassoLine.Points.Add(new Point(p.X * _pixelSize, p.Y * _pixelSize));
                }
            }
            return;
        }
        if (!_drawing || point == null) return;

        var x = point.Value.X;
        var y = point.Value.Y;
        var dx = Math.Abs(x - _lastX);
        var dy = Math.Abs(y - _lastY);
        var steps = Math.Max(dx, dy);
        if (steps == 0) return;
        for (var i = 1; i <= steps; i++)
        {
            Paint(_lastX + (int)Math.Round((x - _lastX) * (double)i / steps),
                  _lastY + (int)Math.Round((y - _lastY) * (double)i / steps));
        }
        _lastX = x;
        _lastY = y;
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_lassoing)
        {
            FinishLasso();
            return;
        }
        if (!_drawing) return;
        _drawing = false;
        SkinImage.ReleaseMouseCapture();
        Recompose();
        Flush();
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        CursorText.Text = "—";
        if (_lassoing)
        {
            CancelLasso();
            return;
        }
        if (!_drawing) return;
        _drawing = false;
        SkinImage.ReleaseMouseCapture();
        Recompose();
        Flush();
    }

    private void FinishLasso()
    {
        _lassoing = false;
        SkinImage.ReleaseMouseCapture();
        if (_lassoLine != null)
        {
            GridOverlay.Children.Remove(_lassoLine);
            _lassoLine = null;
        }
        if (_lassoPoints.Count < 3) return;

        Array.Clear(_selection);
        var count = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!InLasso(x + 0.5, y + 0.5)) continue;
                _selection[y * Size + x] = true;
                count++;
            }
        }
        _selectionActive = count > 0;
        SelectionPanel.Visibility = _selectionActive ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = $"选区：{count} 像素";
        UpdateSelectionOverlay();
        if (_selectionActive) SelectTool("brush");
    }

    private void CancelLasso()
    {
        _lassoing = false;
        _lassoPoints.Clear();
        SkinImage.ReleaseMouseCapture();
        if (_lassoLine == null) return;
        GridOverlay.Children.Remove(_lassoLine);
        _lassoLine = null;
        RebuildOverlay();
    }

    private bool InLasso(double px, double py)
    {
        var inside = false;
        for (int i = 0, j = _lassoPoints.Count - 1; i < _lassoPoints.Count; j = i++)
        {
            var xi = _lassoPoints[i].X;
            var yi = _lassoPoints[i].Y;
            var xj = _lassoPoints[j].X;
            var yj = _lassoPoints[j].Y;
            if (yi > py == yj > py) continue;
            if (px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private void Paint(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= Size || cy >= Size) return;

        if (_tool == "stamp")
        {
            StampAt(cx, cy);
            Recompose();
            Flush();
            return;
        }

        var face = _pixelFace[cy * Size + cx];
        if (face == null) PaintSquare(cx, cy);
        else PaintFace(face, cx - face.U0, cy - face.V0, face.W);
        Recompose();
        Flush();
    }

    private void StampAt(int x, int y)
    {
        if (_stampSource == null) return;
        if (!_stampAnchored)
        {
            _stampAnchorX = x;
            _stampAnchorY = y;
            _stampAnchored = true;
        }
        var sx = _stampSource.Value.X + (x - _stampAnchorX);
        var sy = _stampSource.Value.Y + (y - _stampAnchorY);
        var color = GetCompositePixel(sx, sy);
        var offset = (_brushSize - 1) / 2;
        for (var dy = 0; dy < _brushSize; dy++)
        {
            for (var dx = 0; dx < _brushSize; dx++)
            {
                if (!InBrushShape(dx, dy, offset)) continue;
                SetPixel(x - offset + dx, y - offset + dy, color);
            }
        }
    }

    private bool InBrushShape(int dx, int dy, int offset)
    {
        if (BrushShapeCombo.SelectedIndex != 1 || _brushSize <= 1) return true;
        var cx = offset;
        var cy = offset;
        var radius = _brushSize / 2.0 + 0.1;
        return (dx - cx) * (dx - cx) + (dy - cy) * (dy - cy) <= radius * radius;
    }

    private void PaintFace(FaceTexture face, int lx, int ly, int faceWidth)
    {
        var offset = (_brushSize - 1) / 2;
        var color = _tool == "eraser" ? Colors.Transparent : _color;
        var mirror = SymmetryToggle.IsChecked == true;
        for (var dy = 0; dy < _brushSize; dy++)
        {
            for (var dx = 0; dx < _brushSize; dx++)
            {
                if (!InBrushShape(dx, dy, offset)) continue;
                var x = lx - offset + dx;
                var y = ly - offset + dy;
                if (x < 0 || y < 0 || x >= faceWidth || y >= face.H) continue;
                SetPixel(face.U0 + x, face.V0 + y, color);
                if (mirror) SetPixel(face.U0 + faceWidth - 1 - x, face.V0 + y, color);
            }
        }
    }

    private void PaintSquare(int cx, int cy)
    {
        var offset = (_brushSize - 1) / 2;
        var color = _tool == "eraser" ? Colors.Transparent : _color;
        for (var dy = 0; dy < _brushSize; dy++)
        {
            for (var dx = 0; dx < _brushSize; dx++)
            {
                if (!InBrushShape(dx, dy, offset)) continue;
                SetPixel(cx - offset + dx, cy - offset + dy, color);
            }
        }
    }

    private void SetPixel(int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        if (_selectionActive && !_selection[y * Size + x]) return;
        WritePixel(EditBuffer, (y * Size + x) * 4, color);
    }

    private static void WritePixel(byte[] buffer, int index, Color color)
    {
        if (color.A == 0)
        {
            buffer[index] = 0;
            buffer[index + 1] = 0;
            buffer[index + 2] = 0;
            buffer[index + 3] = 0;
            return;
        }
        buffer[index] = color.B;
        buffer[index + 1] = color.G;
        buffer[index + 2] = color.R;
        buffer[index + 3] = color.A;
    }

    private Color GetCompositePixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return Colors.Transparent;
        var index = (y * Size + x) * 4;
        return Color.FromArgb(_pixels[index + 3], _pixels[index + 2], _pixels[index + 1], _pixels[index]);
    }

    private void Flush()
    {
        _bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), _pixels, Size * 4, 0);
        SchedulePreviewRefresh();
    }

    private void SchedulePreviewRefresh()
    {
        if (!_previewBuilt || _previewTimer == null) return;
        if (_previewTimer.IsEnabled) return;
        _previewTimer.Start();
    }

    private void FloodFill(int x, int y, Color target, byte[] buffer)
    {
        var replacement = _tool == "eraser" ? Colors.Transparent : _color;
        if (target == replacement && _tool != "eraser") return;
        var visited = new bool[Size * Size];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= Size || cy >= Size) continue;
            var index = cy * Size + cx;
            if (visited[index]) continue;
            visited[index] = true;
            if (!Similar(GetCompositePixel(cx, cy), target)) continue;
            if (_selectionActive && !_selection[index]) continue;
            WritePixel(buffer, index * 4, replacement);
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }

    private bool Similar(Color a, Color b)
    {
        if (a.A == 0 && b.A == 0) return true;
        if (a.A == 0 || b.A == 0) return Math.Abs(a.A - b.A) <= _tolerance;
        return Math.Abs(a.R - b.R) <= _tolerance &&
               Math.Abs(a.G - b.G) <= _tolerance &&
               Math.Abs(a.B - b.B) <= _tolerance &&
               Math.Abs(a.A - b.A) <= _tolerance;
    }

    private void SelectByWand(int x, int y)
    {
        var target = GetCompositePixel(x, y);
        Array.Clear(_selection);
        var visited = new bool[Size * Size];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        var count = 0;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= Size || cy >= Size) continue;
            var index = cy * Size + cx;
            if (visited[index]) continue;
            visited[index] = true;
            if (!Similar(GetCompositePixel(cx, cy), target)) continue;
            _selection[index] = true;
            count++;
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
        _selectionActive = count > 0;
        if (_selectionActive) SelectTool("brush");
        UpdateSelectionOverlay();
        RebuildOverlay();
    }

    // ---------- 工具事件 ----------

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is RadioButton { Tag: string tool }) _tool = tool;
    }

    private void BrushSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        _brushSize = BrushSizeCombo.SelectedIndex switch { 1 => 2, 2 => 3, 3 => 4, 4 => 6, _ => 1 };
    }

    private void Tolerance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tolerance = (int)Math.Round(e.NewValue);
        if (ToleranceText != null) ToleranceText.Text = _tolerance.ToString();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo(false);

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo(bool silent)
    {
        if (_undo.Count == 0)
        {
            if (!silent) MessageDialog.Show(Window.GetWindow(this), "没有可撤销的操作", "先画几笔再试试。");
            return;
        }
        _redo.Add(CaptureDoc());
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreDoc(state);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(CaptureDoc());
        var state = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreDoc(state);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Show(Window.GetWindow(this), "新建皮肤",
                "将清空当前画布并开始一张全新的 64×64 皮肤，确定继续吗？", "新建", "取消")) return;
        PushUndo();
        Array.Clear(_base);
        _layers.Clear();
        _active = null;
        ClearSelection();
        Recompose();
        Flush();
        RefreshLayerList();
        SkinSubtitleText.Text = "新皮肤 · 64×64";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        var layer = EnsureLayer();
        Array.Clear(layer.Pixels);
        Recompose();
        Flush();
    }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Show(Window.GetWindow(this), "重置为默认皮肤",
                "当前画布内容会被替换为默认皮肤（史蒂夫），确定继续吗？", "重置", "取消")) return;
        ResetToDefault();
    }

    private void ResetToDefault()
    {
        PushUndo();
        ApplySampleTemplate(_base);
        _layers.Clear();
        _active = null;
        _selectedPart = "head";
        _selectedRegion = null;
        ClearSelection();
        SetPartRadio("head");
        BuildPartRegions();
        Recompose();
        Flush();
        RefreshLayerList();
        RebuildOverlay();
        SkinSubtitleText.Text = "默认皮肤 · 64×64";
    }

    // ---------- 滤镜 ----------

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
        PushUndo();
        var layer = EnsureLayer();
        var buffer = layer.Pixels;
        // 当前图层还没有像素时，先把当前可见结果写入图层，让滤镜作用于整张皮肤
        var empty = true;
        for (var i = 3; i < buffer.Length; i += 4)
        {
            if (buffer[i] == 0) continue;
            empty = false;
            break;
        }
        if (empty)
        {
            Recompose();
            Buffer.BlockCopy(_pixels, 0, buffer, 0, _pixels.Length);
        }
        switch (filter)
        {
            case "gray":
                ForEachPixel(buffer, (i, a) =>
                {
                    var gray = (byte)Math.Round(buffer[i] * 0.114 + buffer[i + 1] * 0.587 + buffer[i + 2] * 0.299);
                    buffer[i] = buffer[i + 1] = buffer[i + 2] = gray;
                });
                break;
            case "bright":
                ForEachPixel(buffer, (i, a) => Adjust(buffer, i, 20));
                break;
            case "dark":
                ForEachPixel(buffer, (i, a) => Adjust(buffer, i, -20));
                break;
            case "contrast":
                ForEachPixel(buffer, (i, a) =>
                {
                    for (var c = i; c < i + 3; c++)
                    {
                        var v = buffer[c] / 255.0;
                        buffer[c] = (byte)Math.Round(Math.Clamp((v - 0.5) * 1.25 + 0.5, 0, 1) * 255);
                    }
                });
                break;
            case "saturate":
            case "desaturate":
                var factor = filter == "saturate" ? 1.3 : 0.6;
                ForEachPixel(buffer, (i, a) =>
                {
                    var gray = buffer[i] * 0.114 + buffer[i + 1] * 0.587 + buffer[i + 2] * 0.299;
                    for (var c = i; c < i + 3; c++)
                        buffer[c] = (byte)Math.Round(Math.Clamp(gray + (buffer[c] - gray) * factor, 0, 255));
                });
                break;
            case "hue":
                ForEachPixel(buffer, (i, a) =>
                {
                    var color = Color.FromArgb(buffer[i + 3], buffer[i + 2], buffer[i + 1], buffer[i]);
                    var (h, s, v) = ColorToHsv(color);
                    var shifted = HsvToColor((h + 15) % 360, s, v, color.A / 255.0);
                    buffer[i] = shifted.B;
                    buffer[i + 1] = shifted.G;
                    buffer[i + 2] = shifted.R;
                });
                break;
            case "posterize":
                ForEachPixel(buffer, (i, a) =>
                {
                    for (var c = i; c < i + 3; c++)
                    {
                        var v = buffer[c] / 255.0;
                        buffer[c] = (byte)Math.Round(Math.Round(v * 3) / 3 * 255);
                    }
                });
                break;
            case "outline":
                ApplyOutline(layer);
                break;
            case "tint":
                ForEachPixel(buffer, (i, a) =>
                {
                    buffer[i] = (byte)(buffer[i] * 0.6 + _color.B * 0.4);
                    buffer[i + 1] = (byte)(buffer[i + 1] * 0.6 + _color.G * 0.4);
                    buffer[i + 2] = (byte)(buffer[i + 2] * 0.6 + _color.R * 0.4);
                });
                break;
            case "gradient":
                ApplyGradient(layer);
                break;
        }
        Recompose();
        Flush();
    }

    private void ForEachPixel(byte[] buffer, Action<int, int> action)
    {
        for (var i = 0; i < buffer.Length; i += 4)
        {
            if (buffer[i + 3] == 0) continue;
            action(i, buffer[i + 3]);
        }
    }

    private static void Adjust(byte[] buffer, int i, int delta)
    {
        for (var c = i; c < i + 3; c++)
            buffer[c] = (byte)Math.Clamp(buffer[c] + delta, 0, 255);
    }

    private void ApplyOutline(SkinLayer layer)
    {
        var snapshot = (byte[])layer.Pixels.Clone();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var index = (y * Size + x) * 4;
                if (snapshot[index + 3] == 0) continue;
                if (x > 0 && snapshot[index - 4 + 3] == 0) WritePixel(layer.Pixels, index - 4, _color);
                if (x < Size - 1 && snapshot[index + 4 + 3] == 0) WritePixel(layer.Pixels, index + 4, _color);
                if (y > 0 && snapshot[index - Size * 4 + 3] == 0) WritePixel(layer.Pixels, index - Size * 4, _color);
                if (y < Size - 1 && snapshot[index + Size * 4 + 3] == 0) WritePixel(layer.Pixels, index + Size * 4, _color);
            }
        }
    }

    private void ApplyGradient(SkinLayer layer)
    {
        var bayer = new[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        for (var y = 0; y < Size; y++)
        {
            var alpha = 1.0 - y / (double)(Size - 1);
            for (var x = 0; x < Size; x++)
            {
                var index = y * Size + x;
                if (_selectionActive && !_selection[index]) continue;
                var threshold = (bayer[(y % 4) * 4 + (x % 4)] + 0.5) / 16.0;
                if (alpha * _color.A / 255.0 * 2.4 < threshold) continue;
                WritePixel(layer.Pixels, index * 4, Color.FromArgb(_color.A, _color.R, _color.G, _color.B));
            }
        }
    }

    // ---------- 颜色 ----------

    private void UpdateColorDisplay()
    {
        CurrentColorBox.Background = new SolidColorBrush(_color);
        CurrentColorText.Text = _color.A == 0
            ? "透明"
            : $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        if (_color.A > 0)
            ColorHexBox.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
    }

    private void ApplyHex_Click(object sender, RoutedEventArgs e)
    {
        var text = ColorHexBox.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            SetColorFromExternal(color);
            SelectTool("brush");
        }
        catch
        {
            MessageDialog.Show(Window.GetWindow(this), "颜色格式不正确", "请输入类似 #E8B48F 的颜色值。", warning: true);
        }
    }

    private void PickTransparent_Click(object sender, RoutedEventArgs e)
    {
        _alpha = 0;
        _color = Colors.Transparent;
        UpdateColorDisplay();
        UpdatePickerVisuals();
        SelectTool("eraser");
    }

    // ---------- 文件 ----------

    private void ImportSkin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入皮肤",
            Filter = "皮肤图片|*.png"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var frame = BitmapDecoder.Create(new Uri(dialog.FileName), BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth == 64 && frame.PixelHeight == 32)
            {
                MessageDialog.Show(Window.GetWindow(this), "不支持旧版皮肤",
                    "这是 64×32 的旧版皮肤格式。请先在画图工具里扩展为 64×64，或在“新版皮肤”网站上转换后再导入。",
                    warning: true);
                return;
            }
            if (frame.PixelWidth != 64 || frame.PixelHeight != 64)
            {
                MessageDialog.Show(Window.GetWindow(this), "尺寸不正确",
                    $"这张图片是 {frame.PixelWidth}×{frame.PixelHeight}，皮肤必须是 64×64 像素。", warning: true);
                return;
            }

            PushUndo();
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(_base, Size * 4, 0);
            _layers.Clear();
            _active = null;
            Recompose();
            Flush();
            RefreshLayerList();
            SkinSubtitleText.Text = $"已载入 {Path.GetFileName(dialog.FileName)} · 64×64";
            Logger.Info("已导入皮肤：" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Logger.Error("导入皮肤失败：" + ex.Message);
            MessageDialog.Show(Window.GetWindow(this), "导入失败", ex.Message, warning: true);
        }
    }

    private void ExportSkin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出皮肤",
            Filter = "PNG 图片|*.png",
            FileName = "skin.png"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SavePng(dialog.FileName);
            var open = MessageDialog.Show(Window.GetWindow(this), "导出完成",
                "皮肤已保存到：\n" + dialog.FileName, "打开文件夹", "完成");
            if (open) Utils.OpenFolder(Path.GetDirectoryName(dialog.FileName) ?? AppState.DataDir);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "导出失败", ex.Message, warning: true);
        }
    }

    private void SaveSkin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = SkinFolder();
            Directory.CreateDirectory(dir);
            var suggestion = "皮肤-" + DateTime.Now.ToString("yyyyMMdd");
            var input = InputDialog.Show(Window.GetWindow(this), "保存到皮肤库",
                "给这张皮肤取个名字，之后可以在「我的皮肤」里找到它：", suggestion);
            if (string.IsNullOrWhiteSpace(input)) return;

            var name = Utils.SafeFileName(input).Trim();
            if (name.Length == 0) name = suggestion;
            var path = Path.Combine(dir, name + ".png");
            var index = 2;
            while (File.Exists(path))
                path = Path.Combine(dir, $"{name} ({index++}).png");
            SavePng(path);
            RefreshLibrary();
            Logger.Info("皮肤已保存到皮肤库：" + path);
            var open = MessageDialog.Show(Window.GetWindow(this), "已保存到皮肤库",
                $"{Path.GetFileName(path)}\n\n位置：{path}\n\n在「我的皮肤」里点「使用」即可让它在游戏里生效。",
                "打开文件夹", "完成");
            if (open) Utils.OpenFolder(dir);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "保存失败", ex.Message, warning: true);
        }
    }

    private void SavePng(string path)
    {
        var source = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, _pixels, Size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string SkinFolder() => Path.Combine(AppState.DataDir, "skins");

    private void RefreshLibrary()
    {
        var dir = SkinFolder();
        var items = new List<SkinItem>();
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.png")
                         .OrderByDescending(f => File.GetLastWriteTime(f))
                         .Take(24))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 56;
                    image.UriSource = new Uri(file, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    items.Add(new SkinItem
                    {
                        Path = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Image = image
                    });
                }
                catch
                {
                    // 损坏的图片直接跳过，不影响列表。
                }
            }
        }
        SkinLibrary.ItemsSource = items;
        LibraryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SkinThumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinItem item }) return;
        try
        {
            var frame = BitmapDecoder.Create(new Uri(item.Path), BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth != 64 || frame.PixelHeight != 64)
            {
                MessageDialog.Show(Window.GetWindow(this), "无法载入", "这张皮肤的尺寸不是 64×64。", warning: true);
                return;
            }
            var source = new byte[Size * Size * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(source, Size * 4, 0);
            PushUndo();

            if (PartReplaceToggle.IsChecked == true)
            {
                foreach (var region in Regions.Where(r => r.Part == _selectedPart))
                {
                    for (var y = region.Y; y < region.Y + region.H; y++)
                    {
                        var offset = (y * Size + region.X) * 4;
                        Buffer.BlockCopy(source, offset, _base, offset, region.W * 4);
                    }
                }
                Recompose();
                Flush();
                SkinSubtitleText.Text = $"已从 {item.Name} 替换{RegionName(_selectedPart, null)}";
                return;
            }

            Buffer.BlockCopy(source, 0, _base, 0, _base.Length);
            _layers.Clear();
            _active = null;
            Recompose();
            Flush();
            RefreshLayerList();
            SkinSubtitleText.Text = $"已载入皮肤库：{item.Name}";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "载入失败", ex.Message, warning: true);
        }
    }

    private void OpenMySkins_Click(object sender, RoutedEventArgs e) => Navigation.Go("myskins");

    private void OpenSkinsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = SkinFolder();
        Directory.CreateDirectory(dir);
        Utils.OpenFolder(dir);
    }

    // ---------- 3D 预览 ----------

    private void BuildPreview()
    {
        PreviewViewport.Children.Clear();
        _camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 18, 150),
            LookDirection = new Vector3D(0, -2, -150),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 34
        };
        PreviewViewport.Camera = _camera;

        // DirectionalLight 的 Direction 是光线传播方向：要照亮某一面，光线方向需与该面法线相反
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x6C, 0x6C, 0x72)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xC4, 0xC6, 0xCC), new Vector3D(-0.3, -0.45, -1)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x4C, 0x4C, 0x52), new Vector3D(-1, 0.1, 0.3)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x38, 0x38, 0x3E), new Vector3D(1, 0.1, 0.3)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x34, 0x34, 0x3A), new Vector3D(0, -1, 0.2)));
        PreviewViewport.Children.Add(new ModelVisual3D { Content = lights });

        _floorModel = BuildFloorGrid();
        _floorModel.Transform = _floorShift;
        _floorShift.OffsetX = 0;
        _floorShift.OffsetZ = 0;
        _floorVisual = new ModelVisual3D
        {
            Content = GridToggle.IsChecked == true ? _floorModel : null
        };
        PreviewViewport.Children.Add(_floorVisual);

        _modelGroup = new Model3DGroup();
        var transform = new Transform3DGroup();
        transform.Children.Add(_modelOffset);
        transform.Children.Add(_scale);
        transform.Children.Add(_yaw);
        transform.Children.Add(_pitch);
        _modelGroup.Transform = transform;
        _faces.Clear();
        _meshToFace.Clear();
        _limbRotations.Clear();
        Array.Clear(_pixelFace);

        var slim = ModelCombo.SelectedIndex == 1;
        var armWidth = slim ? 3 : 4;
        var armX = slim ? 5.5 : 6.0;

        AddPart("head", 0, 0, 32, 0, 8, 8, 8, 0, 28);
        AddPart("body", 16, 16, 16, 32, 8, 12, 4, 0, 18);
        AddPart("rightArm", 40, 16, 40, 32, armWidth, 12, 4, -armX, 18);
        AddPart("leftArm", 32, 48, 48, 48, armWidth, 12, 4, armX, 18);
        AddPart("rightLeg", 0, 16, 0, 32, 4, 12, 4, -2, 6);
        AddPart("leftLeg", 16, 48, 0, 48, 4, 12, 4, 2, 6);

        PreviewViewport.Children.Add(new ModelVisual3D { Content = _modelGroup });
        _previewBuilt = true;
        ApplyRotation();
        ApplyZoom();
        RefreshPreviewTextures();
    }

    // ---------- 行走模式 ----------

    private void WalkToggle_Click(object sender, RoutedEventArgs e) => SetWalkMode(!_walkMode);

    private void SetWalkMode(bool on)
    {
        if (on == _walkMode) return;
        _walkMode = on;
        WalkButton.Content = on ? "退出行走" : "行走模式";
        WalkHintText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RotateHintText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        PartHintText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

        if (on)
        {
            if (Mode3DRadio.IsChecked != true) Mode3DRadio.IsChecked = true;
            _posX = 0;
            _posZ = 0;
            _jumpY = 0;
            _jumpVelocity = 0;
            _walkPhase = 0;
            _facingAngle = 0;
            _camAngle = 0;
            UpdateWalkCamera();
            _walkTimer.Start();
        }
        else
        {
            _walkTimer.Stop();
            _modelOffset.OffsetX = 0;
            _modelOffset.OffsetY = 0;
            _modelOffset.OffsetZ = 0;
            // 重建预览，把相机和姿态恢复到默认
            BuildPreview();
        }
    }

    private void Walk_Tick(object? sender, EventArgs e)
    {
        if (!_walkMode || _camera == null) return;
        // 焦点在输入框时不响应，避免和打字冲突
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        var dx = 0.0;
        var dz = 0.0;
        if (Keyboard.IsKeyDown(Key.W)) dz -= 1;
        if (Keyboard.IsKeyDown(Key.S)) dz += 1;
        if (Keyboard.IsKeyDown(Key.A)) dx -= 1;
        if (Keyboard.IsKeyDown(Key.D)) dx += 1;

        var moving = dx != 0 || dz != 0;
        if (moving)
        {
            var length = Math.Sqrt(dx * dx + dz * dz);
            dx /= length;
            dz /= length;
            const double step = 0.5;
            _posX = Math.Clamp(_posX + dx * step, -30, 30);
            _posZ = Math.Clamp(_posZ + dz * step, -30, 30);

            // 模型正面朝 +Z，朝向角按移动方向平滑过渡
            var target = Math.Atan2(dx, dz) * 180 / Math.PI;
            var delta = (target - _facingAngle + 540) % 360 - 180;
            _facingAngle += delta * 0.3;
            _walkPhase += 0.32;

            if (_jumpY <= 0 && Keyboard.IsKeyDown(Key.Space)) _jumpVelocity = 0.8;
        }

        if (_jumpVelocity != 0 || _jumpY > 0)
        {
            _jumpY += _jumpVelocity;
            _jumpVelocity -= 0.13;
            if (_jumpY <= 0)
            {
                _jumpY = 0;
                _jumpVelocity = 0;
            }
        }

        var swing = moving ? 32 * Math.Sin(_walkPhase) : 0;
        SetLimb("rightArm", -swing);
        SetLimb("leftArm", swing);
        SetLimb("rightLeg", swing);
        SetLimb("leftLeg", -swing);

        // 人物保持在原点，地板按整数倍反向滚动，看起来就是在走动
        _floorShift.OffsetX = -Fraction(_posX);
        _floorShift.OffsetZ = -Fraction(_posZ);
        _modelOffset.OffsetY = _jumpY;
        ((AxisAngleRotation3D)_yaw.Rotation).Angle = _facingAngle;
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private void SetLimb(string part, double angle)
    {
        if (_limbRotations.TryGetValue(part, out var axis)) axis.Angle = angle;
    }

    /// <summary>相机绕人物固定距离环绕，_camAngle 决定从哪个方向看（0 正面 / 180 背面 / 90、-90 侧面）。</summary>
    private void UpdateWalkCamera()
    {
        if (_camera == null) return;
        const double distance = 150;
        var radians = _camAngle * Math.PI / 180;
        _camera.Position = new Point3D(
            Math.Sin(radians) * distance,
            18,
            Math.Cos(radians) * distance);
        _camera.LookDirection = new Vector3D(
            -_camera.Position.X,
            16 - _camera.Position.Y,
            -_camera.Position.Z);
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    /// <summary>地面像素网格：每格对应皮肤上的 1 个像素，关闭“像素网格”时整体隐藏。</summary>
    private Model3DGroup BuildFloorGrid()
    {
        var group = new Model3DGroup();
        var mesh = new MeshGeometry3D();
        var material = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x55, 0x7C, 0x8A, 0x9E)));
        material.Freeze();

        const double half = 42;
        const double width = 0.05;
        const double y = -0.02;

        void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var start = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(start);
            mesh.TriangleIndices.Add(start + 2);
            mesh.TriangleIndices.Add(start + 1);
            mesh.TriangleIndices.Add(start);
            mesh.TriangleIndices.Add(start + 3);
            mesh.TriangleIndices.Add(start + 2);
        }

        for (var i = -half; i <= half; i += 1)
        {
            Quad(new Point3D(-half, y, i - width), new Point3D(half, y, i - width),
                new Point3D(half, y, i + width), new Point3D(-half, y, i + width));
            Quad(new Point3D(i - width, y, -half), new Point3D(i + width, y, -half),
                new Point3D(i + width, y, half), new Point3D(i - width, y, half));
        }

        mesh.Freeze();
        group.Children.Add(new GeometryModel3D(mesh, material));
        return group;
    }

    private void AddPart(string part, int u0, int v0, int ou0, int ov0, int w, int h, int d,
        double cx, double cy)
    {
        // 外层（帽子/外套）逐像素透明无法在 WPF 3D 中实现，改为合成到基础面贴图上
        // 每个部位单独成组并绕关节（顶边中心）旋转，行走模式靠它摆动四肢
        var group = new Model3DGroup();
        var axis = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        group.Transform = new RotateTransform3D(axis, 0, cy + h / 2.0, 0);
        _modelGroup!.Children.Add(group);
        _limbRotations[part] = axis;
        _currentPartGroup = group;
        AddFaces(part, u0, v0, ou0, ov0, w, h, d, cx, cy, 0);
        _currentPartGroup = null;
    }

    private void AddFaces(string part, int u0, int v0, int ou0, int ov0, int w, int h, int d,
        double cx, double cy, double cz)
    {
        var hw = w / 2.0;
        var hh = h / 2.0;
        var hd = d / 2.0;

        // 前
        AddFace(part, u0 + d, v0 + d, ou0 + d, ov0 + d, w, h,
            new Point3D(cx - hw, cy + hh, cz + hd), new Point3D(cx + hw, cy + hh, cz + hd),
            new Point3D(cx + hw, cy - hh, cz + hd), new Point3D(cx - hw, cy - hh, cz + hd),
            new Vector3D(0, 0, 1));
        // 后
        AddFace(part, u0 + d + w + d, v0 + d, ou0 + d + w + d, ov0 + d, w, h,
            new Point3D(cx + hw, cy + hh, cz - hd), new Point3D(cx - hw, cy + hh, cz - hd),
            new Point3D(cx - hw, cy - hh, cz - hd), new Point3D(cx + hw, cy - hh, cz - hd),
            new Vector3D(0, 0, -1));
        // 右
        AddFace(part, u0 + d + w, v0 + d, ou0 + d + w, ov0 + d, d, h,
            new Point3D(cx + hw, cy + hh, cz + hd), new Point3D(cx + hw, cy + hh, cz - hd),
            new Point3D(cx + hw, cy - hh, cz - hd), new Point3D(cx + hw, cy - hh, cz + hd),
            new Vector3D(1, 0, 0));
        // 左
        AddFace(part, u0, v0 + d, ou0, ov0 + d, d, h,
            new Point3D(cx - hw, cy + hh, cz - hd), new Point3D(cx - hw, cy + hh, cz + hd),
            new Point3D(cx - hw, cy - hh, cz + hd), new Point3D(cx - hw, cy - hh, cz - hd),
            new Vector3D(-1, 0, 0));
        // 顶
        AddFace(part, u0 + d, v0, ou0 + d, ov0, w, d,
            new Point3D(cx - hw, cy + hh, cz - hd), new Point3D(cx + hw, cy + hh, cz - hd),
            new Point3D(cx + hw, cy + hh, cz + hd), new Point3D(cx - hw, cy + hh, cz + hd),
            new Vector3D(0, 1, 0));
        // 底
        AddFace(part, u0 + d + w, v0, ou0 + d + w, ov0, w, d,
            new Point3D(cx - hw, cy - hh, cz + hd), new Point3D(cx + hw, cy - hh, cz + hd),
            new Point3D(cx + hw, cy - hh, cz - hd), new Point3D(cx - hw, cy - hh, cz - hd),
            new Vector3D(0, -1, 0));
    }

    private void AddFace(string part, int u0, int v0, int ou0, int ov0, int w, int h,
        Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
    {
        var brush = new ImageBrush { Stretch = Stretch.Fill };
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { p0, p1, p2, p3 },
            TextureCoordinates = new PointCollection
            {
                new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)
            },
            TriangleIndices = new Int32Collection { 0, 2, 1, 0, 3, 2 },
            Normals = new Vector3DCollection { normal, normal, normal, normal }
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        (_currentPartGroup ?? _modelGroup!).Children.Add(model);

        var face = new FaceTexture
        {
            Brush = brush, Mesh = mesh, Part = part,
            U0 = u0, V0 = v0, W = w, H = h,
            OU0 = ou0, OV0 = ov0
        };
        _faces.Add(face);
        _meshToFace[mesh] = face;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = u0 + x;
                var py = v0 + y;
                if (px < 0 || py < 0 || px >= Size || py >= Size) continue;
                _pixelFace[py * Size + px] = face;
            }
        }
    }

    private void RefreshPreviewTextures()
    {
        if (!_previewBuilt) return;
        foreach (var face in _faces)
            face.Brush.ImageSource = ComposeFace(face);
    }

    private BitmapSource ComposeFace(FaceTexture face)
    {
        var w = face.W;
        var h = face.H;
        var bytes = new byte[w * h * 4];
        var bakeOuter = OuterToggle.IsChecked == true;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var index = (y * w + x) * 4;
                CopyPixel(face.U0 + x, face.V0 + y, bytes, index);

                if (bakeOuter)
                {
                    var ox = face.OU0 + x;
                    var oy = face.OV0 + y;
                    if (ox < 0 || oy < 0 || ox >= Size || oy >= Size) continue;
                    var source = (oy * Size + ox) * 4;
                    var alpha = _pixels[source + 3];
                    if (alpha == 0) continue;
                    if (alpha == 255)
                    {
                        bytes[index] = _pixels[source];
                        bytes[index + 1] = _pixels[source + 1];
                        bytes[index + 2] = _pixels[source + 2];
                        bytes[index + 3] = 255;
                        continue;
                    }
                    BlendOver(_pixels, source, bytes, index, alpha);
                }
            }
        }

        // WPF 3D 不支持逐像素透明，透明像素用它附近最近的实色像素补齐，避免出现灰块或洋红块
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var index = (y * w + x) * 4;
                if (bytes[index + 3] != 0) continue;
                if (!TryNearestOpaque(bytes, w, h, x, y, out var r, out var g, out var b)) continue;
                bytes[index] = r;
                bytes[index + 1] = g;
                bytes[index + 2] = b;
                bytes[index + 3] = 255;
            }
        }

        // 按整数倍最近邻放大纹理，抵消 3D 双线性采样造成的模糊
        var grid = GridToggle.IsChecked == true;
        var sw = w * TexScale;
        var sh = h * TexScale;
        var big = new byte[sw * sh * 4];
        for (var y = 0; y < sh; y++)
        {
            var row = (y / TexScale) * w * 4;
            var gridRow = y % TexScale == 0;
            for (var x = 0; x < sw; x++)
            {
                var src = row + (x / TexScale) * 4;
                var dst = (y * sw + x) * 4;
                var onLine = grid && (gridRow || x % TexScale == 0);
                big[dst] = (byte)(onLine ? bytes[src] * 82 / 100 : bytes[src]);
                big[dst + 1] = (byte)(onLine ? bytes[src + 1] * 82 / 100 : bytes[src + 1]);
                big[dst + 2] = (byte)(onLine ? bytes[src + 2] * 82 / 100 : bytes[src + 2]);
                big[dst + 3] = bytes[src + 3];
            }
        }
        return BitmapSource.Create(sw, sh, 96.0 * TexScale, 96.0 * TexScale,
            PixelFormats.Bgra32, null, big, sw * 4);
    }

    private static bool TryNearestOpaque(byte[] bytes, int w, int h, int sx, int sy,
        out byte r, out byte g, out byte b)
    {
        for (var radius = 1; radius <= w + h; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                    var x = sx + dx;
                    var y = sy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    var index = (y * w + x) * 4;
                    if (bytes[index + 3] == 0) continue;
                    r = bytes[index];
                    g = bytes[index + 1];
                    b = bytes[index + 2];
                    return true;
                }
            }
        }
        r = 0xC8;
        g = 0xC8;
        b = 0xC8;
        return false;
    }

    private void CopyPixel(int x, int y, byte[] target, int index)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        var source = (y * Size + x) * 4;
        target[index] = _pixels[source];
        target[index + 1] = _pixels[source + 1];
        target[index + 2] = _pixels[source + 2];
        target[index + 3] = _pixels[source + 3];
    }

    private static void BlendOver(byte[] source, int sourceIndex, byte[] target, int index, int alpha)
    {
        var a = alpha / 255.0;
        var sa = source[sourceIndex + 3] / 255.0;
        var outA = a + sa * (1 - a);
        if (outA <= 0) return;
        target[index] = (byte)Math.Round(Math.Clamp(
            (source[sourceIndex] * a + target[index] * sa * (1 - a)) / outA, 0, 1) * 255);
        target[index + 1] = (byte)Math.Round(Math.Clamp(
            (source[sourceIndex + 1] * a + target[index + 1] * sa * (1 - a)) / outA, 0, 1) * 255);
        target[index + 2] = (byte)Math.Round(Math.Clamp(
            (source[sourceIndex + 2] * a + target[index + 2] * sa * (1 - a)) / outA, 0, 1) * 255);
        target[index + 3] = (byte)Math.Round(outA * 255);
    }

    private void ApplyRotation()
    {
        if (_walkMode) return;
        ((AxisAngleRotation3D)_yaw.Rotation).Angle = _yawAngle;
        ((AxisAngleRotation3D)_pitch.Rotation).Angle = _pitchAngle;
    }

    private void ApplyZoom()
    {
        var s = 1 / _zoom;
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        _scale.ScaleZ = s;
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 滚轮向前（Delta > 0）放大模型
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 0.9 : 1.1), 0.55, 2.4);
        ApplyZoom();
        e.Handled = true;
    }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_walkMode) return;
        var hitPoint = e.GetPosition(PreviewViewport);

        if (TryHit3D(hitPoint, out var hoverFace, out var hoverLx, out var hoverLy))
        {
            _selectedPart = hoverFace.Part;
            SetPartRadio(hoverFace.Part);
            _selectedRegion = null;
            for (var i = 0; i < Regions.Length; i++)
            {
                var region = Regions[i];
                if (region.Part != hoverFace.Part) continue;
                if (hoverLx + hoverFace.U0 < region.X || hoverLx + hoverFace.U0 >= region.X + region.W) continue;
                if (hoverLy + hoverFace.V0 < region.Y || hoverLy + hoverFace.V0 >= region.Y + region.H) continue;
                _selectedRegion = region.Name;
                break;
            }
            BuildPartRegions();
            // 表面点的纹理坐标换算
            var px = hoverFace.U0 + hoverLx;
            var py = hoverFace.V0 + hoverLy;

            if (_tool == "picker")
            {
                var picked = GetCompositePixel(px, py);
                if (picked.A > 0)
                {
                    SetColorFromExternal(picked);
                    SelectTool("brush");
                }
                RebuildOverlay();
                return;
            }

            if (_tool == "wand")
            {
                SelectByWand(px, py);
                return;
            }

            if (_tool == "stamp" && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                _stampSource = (px, py);
                _stampAnchored = false;
                RebuildOverlay();
                return;
            }

            PushUndo();
            EnsureLayer();
            _painting3D = true;
            _stampAnchored = false;
            PaintFace3D(hoverFace, hoverLx, hoverLy);
            Recompose();
            Flush();
            PreviewHost.CaptureMouse();
            return;
        }

        _rotating = true;
        _rotateStart = e.GetPosition(PreviewHost);
        PreviewHost.CaptureMouse();
    }

    private void PaintFace3D(FaceTexture face, int lx, int ly)
    {
        if (_tool == "stamp")
        {
            StampAt(face.U0 + lx, face.V0 + ly);
            return;
        }
        PaintFace(face, lx, ly, face.W);
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(PreviewViewport);

        if (_painting3D)
        {
            if (TryHit3D(position, out var face, out var lx, out var ly))
            {
                PaintFace3D(face, lx, ly);
                Recompose();
                Flush();
                _hoverPixel = (face.U0 + lx, face.V0 + ly);
                RebuildOverlay();
            }
            return;
        }

        if (_rotating)
        {
            var point = e.GetPosition(PreviewHost);
            _yawAngle += (point.X - _rotateStart.X) * 0.6;
            _pitchAngle = Math.Clamp(_pitchAngle - (point.Y - _rotateStart.Y) * 0.4, -30, 30);
            _rotateStart = point;
            ApplyRotation();
            return;
        }

        if (TryHit3D(position, out var hoverFace, out var hoverLx, out var hoverLy))
        {
            _hoverPixel = (hoverFace.U0 + hoverLx, hoverFace.V0 + hoverLy);
            CursorText.Text = $"{hoverFace.Part} {_hoverPixel.Value.X},{_hoverPixel.Value.Y}";
            RebuildOverlay();
        }
        else if (_hoverPixel != null)
        {
            _hoverPixel = null;
            RebuildOverlay();
        }
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_painting3D)
        {
            _painting3D = false;
            PreviewHost.ReleaseMouseCapture();
            Recompose();
            Flush();
        }
        if (!_rotating) return;
        _rotating = false;
        PreviewHost.ReleaseMouseCapture();
    }

    private bool TryHit3D(Point point, out FaceTexture face, out int lx, out int ly)
    {
        face = null!;
        lx = ly = 0;
        if (!_previewBuilt) return false;
        var hit = VisualTreeHelper.HitTest(PreviewViewport, point);
        if (hit is not RayMeshGeometry3DHitTestResult ray) return false;
        if (!_meshToFace.TryGetValue(ray.MeshHit, out var target)) return false;

        var uvs = ray.MeshHit.TextureCoordinates;
        if (uvs.Count < 4) return false;
        var u = uvs[ray.VertexIndex1].X * ray.VertexWeight1
              + uvs[ray.VertexIndex2].X * ray.VertexWeight2
              + uvs[ray.VertexIndex3].X * ray.VertexWeight3;
        var v = uvs[ray.VertexIndex1].Y * ray.VertexWeight1
              + uvs[ray.VertexIndex2].Y * ray.VertexWeight2
              + uvs[ray.VertexIndex3].Y * ray.VertexWeight3;
        face = target;
        lx = Math.Clamp((int)(u * target.W), 0, target.W - 1);
        ly = Math.Clamp((int)(v * target.H), 0, target.H - 1);
        return true;
    }

    private static string RegionName(string part, string? region)
    {
        if (region != null) return region;
        return part switch
        {
            "head" => "头",
            "body" => "身体",
            "rightArm" => "右臂",
            "leftArm" => "左臂",
            "rightLeg" => "右腿",
            _ => "左腿"
        };
    }

    // ---------- 视图模式与身体部位 ----------

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is RadioButton { Tag: string mode }) ApplyViewMode(mode);
    }

    private void ApplyViewMode(string mode)
    {
        switch (mode)
        {
            case "2d":
                Col2D.Width = new GridLength(1, GridUnitType.Star);
                ColGap.Width = new GridLength(0);
                Col3D.Width = new GridLength(0);
                View2DHost.Visibility = Visibility.Visible;
                PreviewHost.Visibility = Visibility.Collapsed;
                break;
            case "split":
                Col2D.Width = new GridLength(1, GridUnitType.Star);
                ColGap.Width = new GridLength(14);
                Col3D.Width = new GridLength(1, GridUnitType.Star);
                View2DHost.Visibility = Visibility.Visible;
                PreviewHost.Visibility = Visibility.Visible;
                break;
            default:
                Col2D.Width = new GridLength(0);
                ColGap.Width = new GridLength(0);
                Col3D.Width = new GridLength(1, GridUnitType.Star);
                View2DHost.Visibility = Visibility.Collapsed;
                PreviewHost.Visibility = Visibility.Visible;
                break;
        }
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ResizeCanvas();
            RebuildOverlay();
        }), DispatcherPriority.Loaded);
    }

    private void Part_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not RadioButton { Tag: string part }) return;
        _selectedPart = part;
        _selectedRegion = null;
        BuildPartRegions();
        RebuildOverlay();
    }

    private void BuildPartRegions()
    {
        if (RegionPanel == null) return;
        RegionPanel.Children.Clear();
        foreach (var region in Regions.Where(r => r.Part == _selectedPart))
        {
            var chip = new Button
            {
                Style = (Style)FindResource("RegionChip"),
                Content = new TextBlock { Text = region.Name, FontSize = 11 },
                Tag = region.Name
            };
            chip.Click += Region_Click;
            RegionPanel.Children.Add(chip);
        }
        PartHint.Text = "已选择：" + RegionName(_selectedPart, null) + "，可继续选择具体区域";
        PartHintText.Text = RegionName(_selectedPart, null);
    }

    private void Region_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        _selectedRegion = name;
        var selectedBrush = (Brush)FindResource("AccentTextBrush");
        var normalBrush = (Brush)FindResource("TextSecondaryBrush");
        foreach (var child in RegionPanel.Children.OfType<Button>())
        {
            if (child.Content is not TextBlock text) continue;
            var isSelected = text.Text == name;
            text.Foreground = isSelected ? selectedBrush : normalBrush;
            text.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
        RebuildOverlay();
        PartHint.Text = "已选择：" + name + "（2D 画布已高亮）";
        PartHintText.Text = name;
    }

    private void SetPartRadio(string part)
    {
        var radio = part switch
        {
            "body" => PartBody,
            "rightArm" => PartRightArm,
            "leftArm" => PartLeftArm,
            "rightLeg" => PartRightLeg,
            "leftLeg" => PartLeftLeg,
            _ => PartHead
        };
        if (radio.IsChecked != true) radio.IsChecked = true;
    }

    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_previewBuilt) return;
        BuildPreview();
    }

    private void Outer_Changed(object sender, RoutedEventArgs e)
    {
        if (!_previewBuilt) return;
        RefreshPreviewTextures();
    }

    // ---------- 示例模板 ----------

    // 默认形象参考史蒂夫：棕发、青色上衣、蓝紫长裤、深灰鞋
    private void ApplySampleTemplate(byte[] target)
    {
        Array.Clear(target);
        if (TryLoadDefaultSkin(target)) return;

        var skin = Color.FromRgb(0xC6, 0x8A, 0x60);
        var hair = Color.FromRgb(0x2B, 0x1C, 0x12);
        var shirt = Color.FromRgb(0x00, 0xA0, 0xA0);
        var shirtShade = Color.FromRgb(0x00, 0x86, 0x86);
        var pants = Color.FromRgb(0x40, 0x40, 0xA0);
        var shoe = Color.FromRgb(0x4A, 0x4A, 0x4A);
        var eyeWhite = Color.FromRgb(0xEE, 0xEE, 0xEE);
        var eyePupil = Color.FromRgb(0x2A, 0x1C, 0x12);
        var mouth = Color.FromRgb(0x7A, 0x4A, 0x35);

        BasicFill(target, 0, 0, 32, 16, skin);
        BasicFill(target, 0, 8, 8, 8, hair);
        BasicFill(target, 16, 8, 8, 8, hair);
        BasicFill(target, 24, 8, 8, 8, hair);
        BasicFill(target, 8, 0, 8, 8, hair);
        BasicFill(target, 8, 8, 8, 8, skin);

        BasicFill(target, 32, 0, 32, 8, hair);
        BasicFill(target, 32, 8, 8, 8, hair);
        BasicFill(target, 48, 8, 8, 8, hair);
        BasicFill(target, 56, 8, 8, 8, hair);
        BasicFill(target, 40, 8, 8, 2, hair);

        BasicFill(target, 9, 11, 2, 2, eyeWhite);
        BasicFill(target, 13, 11, 2, 2, eyeWhite);
        BasicFill(target, 10, 11, 1, 2, eyePupil);
        BasicFill(target, 14, 11, 1, 2, eyePupil);
        BasicFill(target, 10, 14, 4, 1, mouth);

        BasicFill(target, 16, 16, 24, 16, shirt);
        BasicFill(target, 16, 16, 24, 1, shirtShade);
        BasicFill(target, 20, 16, 2, 6, shirtShade);
        BasicFill(target, 26, 16, 2, 6, shirtShade);
        BasicFill(target, 16, 20, 24, 1, shirtShade);

        BasicFill(target, 40, 16, 16, 16, skin);
        BasicFill(target, 40, 20, 16, 4, shirt);
        BasicFill(target, 32, 48, 16, 16, skin);
        BasicFill(target, 32, 52, 16, 4, shirt);

        BasicFill(target, 0, 16, 16, 16, pants);
        BasicFill(target, 0, 16, 16, 12, pants);
        BasicFill(target, 0, 60, 16, 4, shoe);
        BasicFill(target, 16, 48, 16, 16, pants);
        BasicFill(target, 16, 52, 16, 8, pants);
        BasicFill(target, 16, 60, 16, 4, shoe);
    }

    /// <summary>优先读取随程序发布的官方史蒂夫皮肤贴图（64×64）。</summary>
    private static bool TryLoadDefaultSkin(byte[] target)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", "steve.png");
            if (!File.Exists(path)) return false;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (bitmap.PixelWidth != Size || bitmap.PixelHeight != Size) return false;
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(target, Size * 4, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void BasicFill(byte[] target, int x, int y, int width, int height, Color color)
    {
        for (var py = y; py < y + height; py++)
        {
            if (py < 0 || py >= Size) continue;
            for (var px = x; px < x + width; px++)
            {
                if (px < 0 || px >= Size) continue;
                WritePixel(target, (py * Size + px) * 4, color);
            }
        }
    }
}

/// <summary>一个可叠加的绘制图层。</summary>
public sealed class SkinLayer
{
    public string Name = "图层";
    public bool Visible = true;
    public int Opacity = 100;
    public int Blend;
    public byte[] Pixels = new byte[64 * 64 * 4];

    public SkinLayer Clone() => new()
    {
        Name = Name,
        Visible = Visible,
        Opacity = Opacity,
        Blend = Blend,
        Pixels = (byte[])Pixels.Clone()
    };
}

/// <summary>撤销 / 重做使用的完整文档快照。</summary>
public sealed class DocState
{
    public byte[] Base = Array.Empty<byte>();
    public List<SkinLayer> Layers = new();
    public int ActiveIndex = -1;
}

internal sealed class FaceTexture
{
    public ImageBrush Brush = null!;
    public MeshGeometry3D Mesh = null!;
    public string Part = "";
    public int U0, V0, W, H;
    public int OU0, OV0;
}

public sealed class SkinItem
{
    public string Path = "";
    public string Name = "";
    public BitmapSource Image = null!;
}





