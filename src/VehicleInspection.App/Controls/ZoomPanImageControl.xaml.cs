using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VehicleInspection.App.Localization;

namespace VehicleInspection.App.Controls;

public partial class ZoomPanImageControl : UserControl
{
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(ZoomPanImageControl), new PropertyMetadata("Inspection image", OnPlaceholderTextChanged));

    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ZoomPanImageControl)d;
        control.PlaceholderTextBlock.Text = (string)e.NewValue;
    }

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(ZoomPanImageControl), new PropertyMetadata(string.Empty, OnImagePathChanged));

    public static readonly DependencyProperty FodRegionsProperty = DependencyProperty.Register(
        nameof(FodRegions), typeof(ObservableCollection<FodRegion>), typeof(ZoomPanImageControl),
        new PropertyMetadata(null, OnFodRegionsChanged));

    public static readonly DependencyProperty SensitivityLevelProperty = DependencyProperty.Register(
        nameof(SensitivityLevel), typeof(int), typeof(ZoomPanImageControl),
        new PropertyMetadata(3, OnSensitivityChanged));

    public static readonly DependencyProperty ShowRoiOverlayProperty = DependencyProperty.Register(
        nameof(ShowRoiOverlay), typeof(bool), typeof(ZoomPanImageControl),
        new PropertyMetadata(false, OnShowRoiChanged));

    public static readonly DependencyProperty SyncTargetProperty = DependencyProperty.Register(
        nameof(SyncTarget), typeof(ZoomPanImageControl), typeof(ZoomPanImageControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty LicensePlateOverlayProperty = DependencyProperty.Register(
        nameof(LicensePlateOverlay), typeof(string), typeof(ZoomPanImageControl),
        new PropertyMetadata(string.Empty, OnLicensePlateOverlayChanged));

    private Point _lastPoint;
    private bool _isDragging;
    private double _targetScale = 1;
    private bool _scaleUpdateQueued;
    private const double DefaultCanvasWidth = 900;
    private const double DefaultCanvasHeight = 460;

    // ROI
    private readonly List<RoiBoxData> _roiBoxes = new();

    // Image processing
    private BitmapSource? _originalBitmap;
    private WriteableBitmap? _workBitmap;
    private byte[]? _sourcePixels;  // pristine, never modified
    private byte[]? _outputPixels;  // output buffer
    private int _workStride;
    private int _workWidth, _workHeight;
    private double _contrastLevel = 1.0;   // 0.3–2.0, 1.0=normal
    private double _brightnessLevel = 0.0; // -0.3–+0.3, 0=normal
    private bool _rightDragging;
    private bool _rightSide;
    private double _dragStartY;
    private double _dragStartValue;
    private bool _processingPending;

    private bool _initialized;

    public ZoomPanImageControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ScheduleInit();
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleInit(); };
        UpdateScaleBadge();
        Loc.LanguageChanged += (_, _) => RefreshDynamicResources();
    }

    private void RefreshDynamicResources()
    {
        // Force DynamicResource re-evaluation on all children
        LicensePlateBadge.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        var expr = GetValue(PlaceholderTextProperty);
        // Force WPF to re-evaluate the resource reference
        InvalidateProperty(PlaceholderTextProperty);
        PlaceholderTextBlock.Text = PlaceholderText;
    }

    private void ScheduleInit()
    {
        if (_initialized) return;
        // Background priority ensures layout pass has completed
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_initialized) return;
            SyncCanvasSize();
            if (ShowRoiOverlay) { LoadRoiFile(); DrawAllRoiBoxes(); }
            CenterSurface();
            _initialized = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string ImagePath
    {
        get => (string)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public ObservableCollection<FodRegion> FodRegions
    {
        get => (ObservableCollection<FodRegion>)GetValue(FodRegionsProperty);
        set => SetValue(FodRegionsProperty, value);
    }

    public int SensitivityLevel
    {
        get => (int)GetValue(SensitivityLevelProperty);
        set => SetValue(SensitivityLevelProperty, value);
    }

    public bool ShowRoiOverlay
    {
        get => (bool)GetValue(ShowRoiOverlayProperty);
        set => SetValue(ShowRoiOverlayProperty, value);
    }

    public ZoomPanImageControl? SyncTarget
    {
        get => (ZoomPanImageControl?)GetValue(SyncTargetProperty);
        set => SetValue(SyncTargetProperty, value);
    }

    public string LicensePlateOverlay
    {
        get => (string)GetValue(LicensePlateOverlayProperty);
        set => SetValue(LicensePlateOverlayProperty, value);
    }

    public event EventHandler<LicensePlateUpdatedEventArgs>? LicensePlateUpdated;

    // ── DP callbacks ──────────────────────────────────────────

    private static void OnLicensePlateOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ZoomPanImageControl)d;
        var text = (string)e.NewValue;
        if (string.IsNullOrWhiteSpace(text))
        {
            control.LicensePlateBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            control.LicensePlateBadge.Text = text;
            control.LicensePlateBadge.Visibility = Visibility.Visible;
        }
    }

    private string _plateBeforeEdit = string.Empty;

    private void LicensePlateBadge_GotFocus(object sender, RoutedEventArgs e)
    {
        _plateBeforeEdit = LicensePlateBadge.Text;
        LicensePlateBadge.SelectAll();
    }

    private void LicensePlateBadge_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitLicensePlateEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            LicensePlateBadge.Text = _plateBeforeEdit;
            LicensePlateBadge.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void LicensePlateBadge_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitLicensePlateEdit();
    }

    private void CommitLicensePlateEdit()
    {
        var newPlate = LicensePlateBadge.Text.Trim();

        if (string.IsNullOrWhiteSpace(newPlate) || string.Equals(_plateBeforeEdit, newPlate, StringComparison.OrdinalIgnoreCase))
        {
            LicensePlateBadge.Text = _plateBeforeEdit;
            return;
        }

        LicensePlateUpdated?.Invoke(this, new LicensePlateUpdatedEventArgs(_plateBeforeEdit, newPlate));
        _plateBeforeEdit = newPlate;
    }

    private static void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        ((ZoomPanImageControl)d).LoadImage(args.NewValue as string);
    }

    private static void OnFodRegionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ZoomPanImageControl)d;
        if (e.OldValue is ObservableCollection<FodRegion> oc) oc.CollectionChanged -= c.OnFodChanged;
        if (e.NewValue is ObservableCollection<FodRegion> nc) nc.CollectionChanged += c.OnFodChanged;
        c.DrawAllRoiBoxes();
    }

    private static void OnSensitivityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ZoomPanImageControl)d).DrawAllRoiBoxes();
    }

    private static void OnShowRoiChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ZoomPanImageControl)d;
        if ((bool)e.NewValue) { c.LoadRoiFile(); c.DrawAllRoiBoxes(); }
        else { var rm = c.ImageSurface.Children.OfType<Border>().ToList(); rm.ForEach(b => c.ImageSurface.Children.Remove(b)); }
    }

    private void OnFodChanged(object? s, NotifyCollectionChangedEventArgs e) => DrawAllRoiBoxes();

    // ── ROI file ──────────────────────────────────────────────

    private void LoadRoiFile()
    {
        _roiBoxes.Clear();
        const string path = @"D:\image\transaction\roi1.json";
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        var lanes = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, RoiBoxJson>>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (lanes is null) return;
        foreach (var (lk, classes) in lanes)
        {
            if (!int.TryParse(lk.TrimStart('L'), out var lvl)) continue;
            foreach (var (_, box) in classes)
                _roiBoxes.Add(new RoiBoxData { Level = lvl, Label = lk, X = box.X, Y = box.Y, W = box.W, H = box.H });
        }
    }

    private void DrawAllRoiBoxes()
    {
        var rm = ImageSurface.Children.OfType<Border>().ToList();
        foreach (var b in rm) ImageSurface.Children.Remove(b);

        var pw = PanelBackground.Width > 0 ? PanelBackground.Width : DefaultCanvasWidth;
        var ph = PanelBackground.Height > 0 ? PanelBackground.Height : DefaultCanvasHeight;
        const double sw = 8192, sh = 4096;
        var srcA = sw / sh; var pnlA = pw / ph;
        double rw, rh, ox, oy;
        if (srcA > pnlA) { rw = pw; rh = pw / srcA; ox = 0; oy = (ph - rh) / 2; }
        else { rh = ph; rw = ph * srcA; ox = (pw - rw) / 2; oy = 0; }
        var sx = rw / sw; var sy = rh / sh;
        var maxLvl = SensitivityLevel;
        var strokes = new[] { null!, SB(255, 60, 60), SB(255, 160, 60), SB(240, 200, 60), SB(83, 193, 138), SB(100, 160, 200) };

        foreach (var r in _roiBoxes)
        {
            if (r.Level > maxLvl) continue;
            var b = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = strokes[r.Level],
                BorderThickness = new Thickness(2),
                Width = r.W * sx, Height = r.H * sy
            };
            Canvas.SetLeft(b, ox + r.X * sx);
            Canvas.SetTop(b, oy + r.Y * sy);
            Canvas.SetZIndex(b, 100);
            ImageSurface.Children.Add(b);
        }
        ImageSurface.InvalidateVisual();
    }

    private static SolidColorBrush SB(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    // ── Image loading ─────────────────────────────────────────

    private void LoadImage(string? path)
    {
        var hasImage = !string.IsNullOrWhiteSpace(path) && (IsHttpUri(path) || File.Exists(path));
        if (hasImage)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path!, UriKind.Absolute); bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            _originalBitmap = bmp;
            _sourcePixels = null; _outputPixels = null; _workBitmap = null;
            _contrastLevel = 1.0;
            _brightnessLevel = 0.0;
            ApplyImageProcessing();
        }
        else { InspectionImage.Source = null; _originalBitmap = null; _sourcePixels = null; _outputPixels = null; _workBitmap = null; }

        InspectionImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderBody.Visibility = Visibility.Collapsed;
        PlaceholderBeam.Visibility = Visibility.Collapsed;
        PlaceholderRail.Visibility = Visibility.Collapsed;
        PlaceholderModule.Visibility = Visibility.Collapsed;
        PlaceholderWheelLeft.Visibility = Visibility.Collapsed;
        PlaceholderWheelRight.Visibility = Visibility.Collapsed;
        PlaceholderTextBlock.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        PanelBackground.Fill = hasImage
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#101820"))
            : Brushes.Black;
        OnReset(this, new RoutedEventArgs());
    }

    private static bool IsHttpUri(string p) => Uri.TryCreate(p, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    // ── Pixel-level contrast & brightness ─────────────────────

    private void ApplyImageProcessing()
    {
        if (_originalBitmap is null) return;
        if (_contrastLevel == 1.0 && _brightnessLevel == 0.0)
        {
            InspectionImage.Source = _originalBitmap;
            return;
        }

        // Lazy-init buffers from original (once per image load)
        if (_sourcePixels is null)
        {
            var src = new FormatConvertedBitmap(_originalBitmap, PixelFormats.Bgra32, null, 0);
            _workWidth = src.PixelWidth; _workHeight = src.PixelHeight;
            _workBitmap = new WriteableBitmap(_workWidth, _workHeight, 96, 96, PixelFormats.Bgra32, null);
            _workStride = _workWidth * 4;
            _sourcePixels = new byte[_workStride * _workHeight];
            _outputPixels = new byte[_workStride * _workHeight];
            src.CopyPixels(_sourcePixels, _workStride, 0);
        }

        var srcPx = _sourcePixels;
        var outPx = _outputPixels!;
        var stride = _workStride;
        var w = _workWidth; var h = _workHeight;
        var c = _contrastLevel;
        var b = _brightnessLevel;

        // Always compute from pristine source → stable, no drift
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            var off = y * stride;
            for (int x = 0; x < w; x++)
            {
                int i = off + x * 4;
                for (int ch = 0; ch < 3; ch++)
                {
                    var v = (srcPx[i + ch] / 255.0 - 0.5) * c + 0.5 + b;
                    outPx[i + ch] = (byte)(Math.Clamp(v * 255, 0, 255));
                }
                outPx[i + 3] = srcPx[i + 3]; // copy alpha
            }
        });

        _workBitmap!.WritePixels(new Int32Rect(0, 0, w, h), outPx, stride, 0);
        InspectionImage.Source = _workBitmap;
    }

    private void ScheduleProcessing()
    {
        if (_processingPending) return;
        _processingPending = true;
        CompositionTarget.Rendering += DoProcessing;
    }

    private void DoProcessing(object? s, EventArgs e)
    {
        CompositionTarget.Rendering -= DoProcessing;
        _processingPending = false;
        ApplyImageProcessing();
    }

    // ── Zoom / pan / reset ────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _targetScale = Math.Clamp(_targetScale * (e.Delta > 0 ? 1.08 : 0.92), 0.5, 5);
        if (!_scaleUpdateQueued) { _scaleUpdateQueued = true; CompositionTarget.Rendering += ApplyQueuedScale; }
        e.Handled = true;
    }

    private void ApplyQueuedScale(object? s, EventArgs e)
    {
        CompositionTarget.Rendering -= ApplyQueuedScale;
        _scaleUpdateQueued = false;
        ScaleTransform.ScaleX = _targetScale; ScaleTransform.ScaleY = _targetScale;
        UpdateScaleBadge(); SyncToTarget();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true; _lastPoint = e.GetPosition(this);
        Viewport.CaptureMouse(); Viewport.Cursor = Cursors.SizeAll;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false; Viewport.ReleaseMouseCapture(); Viewport.Cursor = Cursors.Arrow;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(this);
        if (pt.X >= ActualWidth * 0.75) _rightSide = true;
        else if (pt.X <= ActualWidth * 0.25) _rightSide = false;
        else return;
        _rightDragging = true; _dragStartY = pt.Y;
        _dragStartValue = _rightSide ? _contrastLevel : _brightnessLevel;
        Viewport.CaptureMouse(); Viewport.Cursor = Cursors.SizeNS;
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _rightDragging = false; Viewport.ReleaseMouseCapture(); Viewport.Cursor = Cursors.Arrow;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_rightDragging)
        {
            var pt = e.GetPosition(this);
            var delta = (_dragStartY - pt.Y) / (ActualHeight * 0.5);
            if (_rightSide)
            {
                _contrastLevel = Math.Clamp(_dragStartValue + delta, 0.3, 2.0);
                Viewport.Tag = $"C:{_contrastLevel:F1}";
            }
            else
            {
                _brightnessLevel = Math.Clamp(_dragStartValue + delta, -0.3, 0.3);
                Viewport.Tag = $"B:{_brightnessLevel:F2}";
            }
            ScheduleProcessing();
            return;
        }
        if (!_isDragging) return;
        var pt2 = e.GetPosition(this);
        TranslateTransform.X += pt2.X - _lastPoint.X;
        TranslateTransform.Y += pt2.Y - _lastPoint.Y;
        _lastPoint = pt2; SyncToTarget();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _targetScale = 1; ScaleTransform.ScaleX = 1; ScaleTransform.ScaleY = 1;
        CenterSurface(); UpdateScaleBadge(); SyncToTarget();
    }

    private void SyncToTarget()
    {
        var t = SyncTarget; if (t is null) return;
        t.ScaleTransform.ScaleX = ScaleTransform.ScaleX;
        t.ScaleTransform.ScaleY = ScaleTransform.ScaleY;
        t.TranslateTransform.X = TranslateTransform.X;
        t.TranslateTransform.Y = TranslateTransform.Y;
        t._targetScale = _targetScale; t.UpdateScaleBadge();
    }

    private void CenterSurface()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        TranslateTransform.X = (ActualWidth - PanelBackground.Width) / 2;
        TranslateTransform.Y = (ActualHeight - PanelBackground.Height) / 2;
    }

    private void UpdateScaleBadge() => Viewport.Tag = $"{_targetScale:P0}";

    // ── Canvas sizing ─────────────────────────────────────────

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => SyncCanvasSize();

    private void SyncCanvasSize()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var pw = ActualWidth - 4; var ph = ActualHeight - 4;
        PanelBackground.Width = pw; PanelBackground.Height = ph;
        InspectionImage.Width = pw; InspectionImage.Height = ph;
        PlaceholderTextBlock.Width = pw; PlaceholderTextBlock.Height = ph;

        var sx = pw / DefaultCanvasWidth; var sy = ph / DefaultCanvasHeight;
        PlaceholderBody.Width = 760 * sx; PlaceholderBody.Height = 120 * sy;
        Canvas.SetLeft(PlaceholderBody, 70 * sx); Canvas.SetTop(PlaceholderBody, 170 * sy);
        PlaceholderBeam.X1 = 90 * sx; PlaceholderBeam.X2 = 810 * sx;
        PlaceholderBeam.Y1 = 230 * sy; PlaceholderBeam.Y2 = 230 * sy;
        PlaceholderRail.X1 = 90 * sx; PlaceholderRail.X2 = 810 * sx;
        PlaceholderRail.Y1 = 255 * sy; PlaceholderRail.Y2 = 255 * sy;
        PlaceholderModule.Width = 220 * sx; PlaceholderModule.Height = 42 * sy;
        Canvas.SetLeft(PlaceholderModule, 340 * sx); Canvas.SetTop(PlaceholderModule, 205 * sy);
        PlaceholderWheelLeft.Width = 54 * sx; PlaceholderWheelLeft.Height = 54 * sy;
        Canvas.SetLeft(PlaceholderWheelLeft, 250 * sx); Canvas.SetTop(PlaceholderWheelLeft, 222 * sy);
        PlaceholderWheelRight.Width = 54 * sx; PlaceholderWheelRight.Height = 54 * sy;
        Canvas.SetLeft(PlaceholderWheelRight, 596 * sx); Canvas.SetTop(PlaceholderWheelRight, 222 * sy);
        CenterSurface();
    }

    // ── Nested types ──────────────────────────────────────────

    private sealed class RoiBoxJson { public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } }
    private sealed class RoiBoxData { public int Level { get; init; } public string Label { get; init; } = ""; public double X { get; init; } public double Y { get; init; } public double W { get; init; } public double H { get; init; } }
}

public sealed class LicensePlateUpdatedEventArgs : EventArgs
{
    public string OldPlate { get; }
    public string NewPlate { get; }
    public LicensePlateUpdatedEventArgs(string oldPlate, string newPlate) { OldPlate = oldPlate; NewPlate = newPlate; }
}

public sealed class FodRegion
{
    public double X { get; init; } public double Y { get; init; }
    public double Width { get; init; } public double Height { get; init; }
    public string Label { get; init; } = ""; public string SeverityLevel { get; init; } = "L3";
    public Brush FillBrush { get; init; } = Brushes.Red; public Brush StrokeBrush { get; init; } = Brushes.DarkRed;
}
