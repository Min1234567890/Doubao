using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VehicleInspection.App.Controls;

public partial class ZoomPanImageControl : UserControl
{
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(ZoomPanImageControl), new PropertyMetadata("Inspection image"));

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(ZoomPanImageControl), new PropertyMetadata(string.Empty, OnImagePathChanged));

    private Point _lastPoint;
    private bool _isDragging;
    private double _targetScale = 1;
    private bool _scaleUpdateQueued;

    public ZoomPanImageControl()
    {
        InitializeComponent();
        Loaded += (_, _) => CenterSurface();
        SizeChanged += (_, _) => CenterSurface();
        UpdateScaleBadge();
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

    private static void OnImagePathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((ZoomPanImageControl)dependencyObject).LoadImage(args.NewValue as string);
    }

    private void LoadImage(string? path)
    {
        var hasImage = !string.IsNullOrWhiteSpace(path) && (IsHttpUri(path) || System.IO.File.Exists(path));
        if (hasImage)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path!, UriKind.Absolute);
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }
            InspectionImage.Source = bitmap;
        }
        else
        {
            InspectionImage.Source = null;
        }

        InspectionImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        var placeholderVisibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderBody.Visibility = placeholderVisibility;
        PlaceholderBeam.Visibility = placeholderVisibility;
        PlaceholderRail.Visibility = placeholderVisibility;
        PlaceholderModule.Visibility = placeholderVisibility;
        PlaceholderWheelLeft.Visibility = placeholderVisibility;
        PlaceholderWheelRight.Visibility = placeholderVisibility;
        PlaceholderTextBlock.Visibility = placeholderVisibility;
        OnReset(this, new RoutedEventArgs());
    }

    private static bool IsHttpUri(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _targetScale = Math.Clamp(_targetScale * (e.Delta > 0 ? 1.08 : 0.92), 0.5, 5);

        if (!_scaleUpdateQueued)
        {
            _scaleUpdateQueued = true;
            CompositionTarget.Rendering += ApplyQueuedScale;
        }

        e.Handled = true;
    }

    private void ApplyQueuedScale(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= ApplyQueuedScale;
        _scaleUpdateQueued = false;
        ScaleTransform.ScaleX = _targetScale;
        ScaleTransform.ScaleY = _targetScale;
        UpdateScaleBadge();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastPoint = e.GetPosition(this);
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.SizeAll;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        TranslateTransform.X += currentPoint.X - _lastPoint.X;
        TranslateTransform.Y += currentPoint.Y - _lastPoint.Y;
        _lastPoint = currentPoint;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _targetScale = 1;
        ScaleTransform.ScaleX = 1;
        ScaleTransform.ScaleY = 1;
        CenterSurface();
        UpdateScaleBadge();
    }

    private void CenterSurface()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        TranslateTransform.X = (ActualWidth - PanelBackground.Width) / 2;
        TranslateTransform.Y = (ActualHeight - PanelBackground.Height) / 2;
    }

    private void UpdateScaleBadge()
    {
        Viewport.Tag = $"{_targetScale:P0}";
    }
}
