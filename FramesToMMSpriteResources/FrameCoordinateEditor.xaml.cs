using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FramesToMMSpriteResources;

public sealed partial class FrameCoordinateEditor : UserControl
{
    private Vector2 _pan = Vector2.Zero;
    private IntVector2 _spritePosition = new IntVector2(0,0);
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPan;
    private bool _isDragging;
    private float _zoom = 1.0f;
    private const float MinZoom = 0.3f;
    private const float MaxZoom = 18.0f;
    private WriteableBitmap? _checkerBitmap;
    private byte[]? _checkerPixels;

    private byte[]? _spriteSourcePixels;
    private int _spriteSourceWidth;
    private int _spriteSourceHeight;
    private int _spriteRenderedWidth = -1;
    private int _spriteRenderedHeight = -1;
    private WriteableBitmap? _spriteBitmap;
    private CancellationTokenSource? _spriteRenderCts;
    private readonly DispatcherQueueTimer _spriteRenderTimer;
    private int _pendingSpriteWidth;
    private int _pendingSpriteHeight;
    private const int CheckerRenderScale = 1;
    private DateTime _lastInteractionUtc = DateTime.MinValue;
    private bool _isUpdatingZoomControls;

    public FrameCoordinateEditor()
    {
        this.InitializeComponent();
        VectorXTextBox.Value = 0;
        VectorYTextBox.Value = 0;
        ZoomSlider.Value = 100;
        ZoomNumberBox.Value = 100;
        _spriteRenderTimer = DispatcherQueue.CreateTimer();
        _spriteRenderTimer.Interval = TimeSpan.FromMilliseconds(120);
        _spriteRenderTimer.IsRepeating = false;
        _spriteRenderTimer.Tick += (_, _) =>
        {
            _spriteRenderTimer.Stop();
            StartSpriteBitmapRender(_pendingSpriteWidth, _pendingSpriteHeight);
        };
        _ = SetSpriteImageUriAsync("ms-appx:///Assets/icon.png");
        UpdateVisuals();
    }

    public IntVector2 SpritePosition
    {
        get => _spritePosition;
        set
        {
            _spritePosition = value;
            VectorXTextBox.Value = value.X;
            VectorYTextBox.Value = value.Y;
            UpdateVisuals();
        }
    }

    public event Action<IntVector2>? SpritePositionChanged;

    public async System.Threading.Tasks.Task SetSpriteImageUriAsync(string uri)
    {
        StorageFile file;
        if (Path.IsPathRooted(uri))
        {
            file = await StorageFile.GetFileFromPathAsync(uri);
        }
        else
        {
            file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
        }
        using IRandomAccessStream stream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        _spriteSourcePixels = pixelData.DetachPixelData();
        _spriteSourceWidth = (int)decoder.PixelWidth;
        _spriteSourceHeight = (int)decoder.PixelHeight;
        _spriteRenderedWidth = -1;
        _spriteRenderedHeight = -1;
        _spriteRenderCts?.Cancel();
        _spriteRenderTimer.Stop();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        double canvasWidth = CoordinateCanvas.ActualWidth;
        double canvasHeight = CoordinateCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        double centerX = canvasWidth / 2.0;
        double centerY = canvasHeight / 2.0;
        double axisX = centerX + _pan.X;
        double axisY = centerY + _pan.Y;

        UpdateCheckerboard(canvasWidth, canvasHeight, axisX, axisY);
        CheckerboardImage.Width = _checkerBitmap?.PixelWidth ?? canvasWidth;
        CheckerboardImage.Height = _checkerBitmap?.PixelHeight ?? canvasHeight;
        Canvas.SetLeft(CheckerboardImage, 0);
        Canvas.SetTop(CheckerboardImage, 0);

        XAxis.Width = canvasWidth;
        Canvas.SetLeft(XAxis, 0);
        Canvas.SetTop(XAxis, axisY - (XAxis.Height / 2.0));

        YAxis.Height = canvasHeight;
        Canvas.SetLeft(YAxis, axisX - (YAxis.Width / 2.0));
        Canvas.SetTop(YAxis, 0);

        double spriteWidth = Math.Max(1.0, _spriteSourceWidth * _zoom);
        double spriteHeight = Math.Max(1.0, _spriteSourceHeight * _zoom);
        RequestSpriteBitmapUpdate(Math.Max(1, (int)Math.Round(spriteWidth)), Math.Max(1, (int)Math.Round(spriteHeight)));

        double spriteCanvasX = axisX + (_spritePosition.X * _zoom);
        double spriteCanvasY = axisY - (_spritePosition.Y * _zoom);
        SpriteImage.Width = spriteWidth;
        SpriteImage.Height = spriteHeight;
        Canvas.SetLeft(SpriteImage, spriteCanvasX - (spriteWidth / 2.0));
        Canvas.SetTop(SpriteImage, spriteCanvasY - (spriteHeight / 2.0));

        UpdateZoomControls();
    }

    private void UpdateCheckerboard(double canvasWidth, double canvasHeight, double axisX, double axisY)
    {
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(canvasWidth / CheckerRenderScale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(canvasHeight / CheckerRenderScale));
        int byteCount = pixelWidth * pixelHeight * 4;

        if (_checkerBitmap == null || _checkerBitmap.PixelWidth != pixelWidth || _checkerBitmap.PixelHeight != pixelHeight)
        {
            _checkerBitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _checkerPixels = new byte[byteCount];
            CheckerboardImage.Source = _checkerBitmap;
        }
        else if (_checkerPixels == null || _checkerPixels.Length != byteCount)
        {
            _checkerPixels = new byte[byteCount];
        }

        double tileSize = 4.0 * _zoom;
        int idx = 0;
        for (int y = 0; y < pixelHeight; y++)
        {
            double sampledCanvasY = y * CheckerRenderScale;
            int tileY = (int)Math.Floor((axisY - sampledCanvasY) / tileSize);
            for (int x = 0; x < pixelWidth; x++)
            {
                double sampledCanvasX = x * CheckerRenderScale;
                int tileX = (int)Math.Floor((sampledCanvasX - axisX) / tileSize);
                byte color = ((tileX + tileY) & 1) == 0 ? (byte)30 : (byte)60;

                _checkerPixels![idx++] = color;
                _checkerPixels[idx++] = color;
                _checkerPixels[idx++] = color;
                _checkerPixels[idx++] = 255;
            }
        }

        using Stream stream = _checkerBitmap!.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(_checkerPixels!, 0, _checkerPixels!.Length);
        _checkerBitmap.Invalidate();
    }

    private void RequestSpriteBitmapUpdate(int targetWidth, int targetHeight)
    {
        if (_spriteSourcePixels == null || _spriteSourceWidth <= 0 || _spriteSourceHeight <= 0)
        {
            return;
        }

        if (_spriteBitmap != null && _spriteRenderedWidth == targetWidth && _spriteRenderedHeight == targetHeight)
        {
            SpriteImage.Source = _spriteBitmap;
            return;
        }

        _spriteRenderedWidth = targetWidth;
        _spriteRenderedHeight = targetHeight;
        _pendingSpriteWidth = targetWidth;
        _pendingSpriteHeight = targetHeight;

        _spriteRenderCts?.Cancel();
        _spriteRenderTimer.Stop();
        _spriteRenderTimer.Start();
    }

    private void StartSpriteBitmapRender(int targetWidth, int targetHeight)
    {
        if (_spriteSourcePixels == null || _spriteSourceWidth <= 0 || _spriteSourceHeight <= 0)
        {
            return;
        }

        if (_isDragging || (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 120)
        {
            _spriteRenderTimer.Stop();
            _spriteRenderTimer.Start();
            return;
        }

        _spriteRenderCts = new CancellationTokenSource();
        CancellationToken token = _spriteRenderCts.Token;
        byte[] sourcePixels = _spriteSourcePixels;
        int sourceWidth = _spriteSourceWidth;
        int sourceHeight = _spriteSourceHeight;

        _ = RenderSpriteBitmapAsync(sourcePixels, sourceWidth, sourceHeight, targetWidth, targetHeight, token);
    }

    private async Task RenderSpriteBitmapAsync(
        byte[] sourcePixels,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        CancellationToken token)
    {
        byte[] scaled;
        try
        {
            scaled = await Task.Run(() =>
            {
                byte[] localScaled = new byte[targetWidth * targetHeight * 4];

                for (int y = 0; y < targetHeight; y++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return localScaled;
                    }

                    int sy = Math.Min(sourceHeight - 1, (int)((y / (double)targetHeight) * sourceHeight));
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int sx = Math.Min(sourceWidth - 1, (int)((x / (double)targetWidth) * sourceWidth));
                        int src = ((sy * sourceWidth) + sx) * 4;
                        int dst = ((y * targetWidth) + x) * 4;
                        localScaled[dst] = sourcePixels[src];
                        localScaled[dst + 1] = sourcePixels[src + 1];
                        localScaled[dst + 2] = sourcePixels[src + 2];
                        localScaled[dst + 3] = sourcePixels[src + 3];
                    }
                }

                return localScaled;
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested ||
            targetWidth != _spriteRenderedWidth ||
            targetHeight != _spriteRenderedHeight)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested ||
                targetWidth != _spriteRenderedWidth ||
                targetHeight != _spriteRenderedHeight)
            {
                return;
            }

            _spriteBitmap = new WriteableBitmap(targetWidth, targetHeight);
            using Stream stream = _spriteBitmap.PixelBuffer.AsStream();
            stream.Position = 0;
            stream.Write(scaled, 0, scaled.Length);
            _spriteBitmap.Invalidate();
            SpriteImage.Source = _spriteBitmap;
        });
    }

    private void CoordinateCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void CoordinateCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CoordinateCanvas);
        _dragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _dragStartPan = _pan;
        _isDragging = true;
        _lastInteractionUtc = DateTime.UtcNow;
        CoordinateCanvas.CapturePointer(e.Pointer);
    }

    private void CoordinateCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(CoordinateCanvas);
        var currentPosition = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _pan = _dragStartPan + (currentPosition - _dragStartPointer);
        _lastInteractionUtc = DateTime.UtcNow;
        UpdateVisuals();
    }

    private void CoordinateCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        CoordinateCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void CoordinateCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CoordinateCanvas);
        int wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        float oldZoom = _zoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = CoordinateCanvas.ActualWidth / 2.0;
        double centerY = CoordinateCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + _pan.X;
        double oldAxisY = centerY + _pan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        _zoom = newZoom;
        _lastInteractionUtc = DateTime.UtcNow;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);

        _pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        UpdateVisuals();
        e.Handled = true;
    }

    private void UpdateZoomControls()
    {
        if (_isUpdatingZoomControls)
        {
            return;
        }

        _isUpdatingZoomControls = true;
        double zoomPercent = Math.Round(_zoom * 100);
        ZoomSlider.Value = zoomPercent;
        ZoomNumberBox.Value = zoomPercent;
        _isUpdatingZoomControls = false;
    }

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingZoomControls)
        {
            return;
        }

        float newZoom = Math.Clamp((float)e.NewValue / 100.0f, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001f)
        {
            return;
        }

        _zoom = newZoom;
        _lastInteractionUtc = DateTime.UtcNow;
        UpdateVisuals();
    }

    private void ZoomNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingZoomControls || double.IsNaN(sender.Value))
        {
            return;
        }

        float newZoom = Math.Clamp((float)sender.Value / 100.0f, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001f)
        {
            return;
        }

        _zoom = newZoom;
        _lastInteractionUtc = DateTime.UtcNow;
        UpdateVisuals();
    }

    private void CenterOriginButton_Click(object sender, RoutedEventArgs e)
    {
        _pan = Vector2.Zero;
        _lastInteractionUtc = DateTime.UtcNow;
        UpdateVisuals();
    }

    private void VectorXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _spritePosition = new IntVector2(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, _spritePosition.Y);
        UpdateVisuals();
        SpritePositionChanged?.Invoke(_spritePosition);
    }

    private void VectorYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _spritePosition = new IntVector2(_spritePosition.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value);
        UpdateVisuals();
        SpritePositionChanged?.Invoke(_spritePosition);
    }

    private void ALignDownButton_Click(object sender, RoutedEventArgs e)
    {
        VectorXTextBox.Value = 0;
        VectorYTextBox.Value = _spriteSourceHeight / 2;
    }

    private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
    {
        VectorXTextBox.Value = (_spriteSourceWidth / 2);
        VectorYTextBox.Value = -(_spriteSourceHeight / 2);
    }

    private void ALignCenterButton_Click(object sender, RoutedEventArgs e)
    {
        VectorXTextBox.Value = 0;
        VectorYTextBox.Value = 0;
    }
}
