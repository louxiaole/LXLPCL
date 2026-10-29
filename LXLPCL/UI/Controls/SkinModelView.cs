using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LXLPCL.UI.Controls;

/// <summary>皮肤 3D 预览：模型、UV 与光照和皮肤制作页保持一致，支持拖动旋转与滚轮缩放。</summary>
public class SkinModelView : Grid
{
    private const int Size = 64;
    private const int TexScale = 8;

    public static readonly DependencyProperty SkinProperty = DependencyProperty.Register(
        nameof(Skin), typeof(ImageSource), typeof(SkinModelView),
        new PropertyMetadata(null, (d, _) => ((SkinModelView)d).Rebuild()));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(SkinModelView), new PropertyMetadata(false));

    public static readonly DependencyProperty YawProperty = DependencyProperty.Register(
        nameof(Yaw), typeof(double), typeof(SkinModelView),
        new PropertyMetadata(-28.0, (d, _) => ((SkinModelView)d).ApplyTransform()));

    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(SkinModelView),
        new PropertyMetadata(0.0, (d, _) => ((SkinModelView)d).ApplyTransform()));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(SkinModelView),
        new PropertyMetadata(1.0, (d, _) => ((SkinModelView)d).ApplyTransform()));

    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly Model3DGroup _model = new();
    private readonly ScaleTransform3D _scale = new(1, 1, 1, 0, 16, 0);
    private readonly RotateTransform3D _yaw = new(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -28), 0, 16, 0);
    private readonly RotateTransform3D _pitch = new(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), 0, 16, 0);

    private Point _dragStart;
    private bool _dragging;
    private double _yawStart;
    private double _pitchStart;

    public SkinModelView()
    {
        ClipToBounds = true;
        Children.Add(_viewport);

        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 17, 100),
            LookDirection = new Vector3D(0, -1, -100),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 34
        };

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x6C, 0x6C, 0x72)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xC4, 0xC6, 0xCC), new Vector3D(-0.3, -0.45, -1)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x4C, 0x4C, 0x52), new Vector3D(-1, 0.1, 0.3)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x38, 0x38, 0x3E), new Vector3D(1, 0.1, 0.3)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x34, 0x34, 0x3A), new Vector3D(0, -1, 0.2)));
        _viewport.Children.Add(new ModelVisual3D { Content = lights });

        var transform = new Transform3DGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(_yaw);
        transform.Children.Add(_pitch);
        _model.Transform = transform;
        _viewport.Children.Add(new ModelVisual3D { Content = _model });

        Loaded += (_, _) =>
        {
            if (!Interactive) return;
            _viewport.Cursor = Cursors.Hand;
        };
    }

    public ImageSource? Skin
    {
        get => (ImageSource?)GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    public double Yaw
    {
        get => (double)GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    public double Pitch
    {
        get => (double)GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Interactive) return;
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _yawStart = Yaw;
        _pitchStart = Pitch;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var point = e.GetPosition(this);
        Yaw = _yawStart + (point.X - _dragStart.X) * 0.55;
        Pitch = Math.Clamp(_pitchStart - (point.Y - _dragStart.Y) * 0.4, -70, 70);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Interactive) return;
        // 滚轮向前（Delta > 0）放大模型
        Zoom = Math.Clamp(Zoom - e.Delta / 1200.0, 0.75, 2.4);
        e.Handled = true;
    }

    private void ApplyTransform()
    {
        ((AxisAngleRotation3D)_yaw.Rotation).Angle = Yaw;
        ((AxisAngleRotation3D)_pitch.Rotation).Angle = Pitch;
        var s = 1 / Zoom;
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        _scale.ScaleZ = s;
    }

    /// <summary>按当前像素重建模型；贴图逐面生成，外层（帽子/外套）烘焙进基础面。</summary>
    private void Rebuild()
    {
        _model.Children.Clear();
        if (Skin is not BitmapSource { PixelWidth: Size, PixelHeight: Size } bitmap) return;
        var frame = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[Size * Size * 4];
        frame.CopyPixels(pixels, Size * 4, 0);

        AddPart(pixels, 0, 0, 32, 0, 8, 8, 8, 0, 28);
        AddPart(pixels, 16, 16, 16, 32, 8, 12, 4, 0, 18);
        AddPart(pixels, 40, 16, 40, 32, 4, 12, 4, -6, 18);
        AddPart(pixels, 32, 48, 48, 48, 4, 12, 4, 6, 18);
        AddPart(pixels, 0, 16, 0, 32, 4, 12, 4, -2, 6);
        AddPart(pixels, 16, 48, 0, 48, 4, 12, 4, 2, 6);
        ApplyTransform();
    }

    private void AddPart(byte[] pixels, int u0, int v0, int ou0, int ov0, int w, int h, int d,
        double cx, double cy)
    {
        var hw = w / 2.0;
        var hh = h / 2.0;
        var hd = d / 2.0;
        const double cz = 0;

        AddFace(pixels, u0 + d, v0 + d, ou0 + d, ov0 + d, w, h,
            new Point3D(cx - hw, cy + hh, cz + hd), new Point3D(cx + hw, cy + hh, cz + hd),
            new Point3D(cx + hw, cy - hh, cz + hd), new Point3D(cx - hw, cy - hh, cz + hd),
            new Vector3D(0, 0, 1));
        AddFace(pixels, u0 + d + w + d, v0 + d, ou0 + d + w + d, ov0 + d, w, h,
            new Point3D(cx + hw, cy + hh, cz - hd), new Point3D(cx - hw, cy + hh, cz - hd),
            new Point3D(cx - hw, cy - hh, cz - hd), new Point3D(cx + hw, cy - hh, cz - hd),
            new Vector3D(0, 0, -1));
        AddFace(pixels, u0 + d + w, v0 + d, ou0 + d + w, ov0 + d, d, h,
            new Point3D(cx + hw, cy + hh, cz + hd), new Point3D(cx + hw, cy + hh, cz - hd),
            new Point3D(cx + hw, cy - hh, cz - hd), new Point3D(cx + hw, cy - hh, cz + hd),
            new Vector3D(1, 0, 0));
        AddFace(pixels, u0, v0 + d, ou0, ov0 + d, d, h,
            new Point3D(cx - hw, cy + hh, cz - hd), new Point3D(cx - hw, cy + hh, cz + hd),
            new Point3D(cx - hw, cy - hh, cz + hd), new Point3D(cx - hw, cy - hh, cz - hd),
            new Vector3D(-1, 0, 0));
        AddFace(pixels, u0 + d, v0, ou0 + d, ov0, w, d,
            new Point3D(cx - hw, cy + hh, cz - hd), new Point3D(cx + hw, cy + hh, cz - hd),
            new Point3D(cx + hw, cy + hh, cz + hd), new Point3D(cx - hw, cy + hh, cz + hd),
            new Vector3D(0, 1, 0));
        AddFace(pixels, u0 + d + w, v0, ou0 + d + w, ov0, w, d,
            new Point3D(cx - hw, cy - hh, cz + hd), new Point3D(cx + hw, cy - hh, cz + hd),
            new Point3D(cx + hw, cy - hh, cz - hd), new Point3D(cx - hw, cy - hh, cz - hd),
            new Vector3D(0, -1, 0));
    }

    private void AddFace(byte[] pixels, int u0, int v0, int ou0, int ov0, int w, int h,
        Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
    {
        var brush = new ImageBrush(ComposeFace(pixels, u0, v0, ou0, ov0, w, h))
        {
            Stretch = Stretch.Fill
        };
        var material = new DiffuseMaterial(brush);
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
        _model.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }

    private static BitmapSource ComposeFace(byte[] pixels, int u0, int v0, int ou0, int ov0, int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var index = (y * w + x) * 4;
                CopyPixel(pixels, u0 + x, v0 + y, bytes, index);

                var ox = ou0 + x;
                var oy = ov0 + y;
                if (ox < 0 || oy < 0 || ox >= Size || oy >= Size) continue;
                var source = (oy * Size + ox) * 4;
                var alpha = pixels[source + 3];
                if (alpha == 0) continue;
                var a = alpha / 255.0;
                if (a >= 1)
                {
                    bytes[index] = pixels[source];
                    bytes[index + 1] = pixels[source + 1];
                    bytes[index + 2] = pixels[source + 2];
                    bytes[index + 3] = 255;
                    continue;
                }
                bytes[index] = (byte)(pixels[source] * a + bytes[index] * (1 - a));
                bytes[index + 1] = (byte)(pixels[source + 1] * a + bytes[index + 1] * (1 - a));
                bytes[index + 2] = (byte)(pixels[source + 2] * a + bytes[index + 2] * (1 - a));
                bytes[index + 3] = 255;
            }
        }

        // WPF 3D 不支持逐像素透明，用最近的实色像素补齐，避免灰块
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

        // 整数倍最近邻放大，抵消 3D 双线性采样造成的模糊
        var sw = w * TexScale;
        var sh = h * TexScale;
        var big = new byte[sw * sh * 4];
        for (var y = 0; y < sh; y++)
        {
            var row = (y / TexScale) * w * 4;
            for (var x = 0; x < sw; x++)
            {
                var src = row + (x / TexScale) * 4;
                var dst = (y * sw + x) * 4;
                big[dst] = bytes[src];
                big[dst + 1] = bytes[src + 1];
                big[dst + 2] = bytes[src + 2];
                big[dst + 3] = bytes[src + 3];
            }
        }
        var bitmap = BitmapSource.Create(sw, sh, 96.0 * TexScale, 96.0 * TexScale,
            PixelFormats.Bgra32, null, big, sw * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void CopyPixel(byte[] pixels, int x, int y, byte[] target, int index)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        var source = (y * Size + x) * 4;
        target[index] = pixels[source];
        target[index + 1] = pixels[source + 1];
        target[index + 2] = pixels[source + 2];
        target[index + 3] = pixels[source + 3];
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
}
