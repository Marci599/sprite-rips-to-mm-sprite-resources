using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FramesToMMSpriteResources;

public sealed partial class FrameCoordinateEditor : UserControl
{
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _spritePosition = Vector2.Zero;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPan;
    private bool _isDragging;
    private float _zoom = 1.0f;
    private const float MinZoom = 0.3f;
    private const float MaxZoom = 18.0f;
    private WriteableBitmap? _checkerBitmap;
    private byte[]? _checkerPixels;

    private int _spriteSourceWidth;
    private int _spriteSourceHeight;
    private bool _checkerboardNeedsRefresh = true;

    public FrameCoordinateEditor()
    {
        this.InitializeComponent();
        VectorXTextBox.Value = 0;
        VectorYTextBox.Value = 0;
        _ = SetSpriteImageUriAsync("ms-appx:///Assets/icon.png");
        UpdateVisuals();
    }

    public Vector2 SpritePosition
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

    public event Action<Vector2>? SpritePositionChanged;

    public async System.Threading.Tasks.Task SetSpriteImageUriAsync(string uri)
    {
        StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
        using IRandomAccessStream stream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        _spriteSourceWidth = (int)decoder.PixelWidth;
        _spriteSourceHeight = (int)decoder.PixelHeight;
        SpriteImage.Source = new BitmapImage(new Uri(uri));
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

        if (_checkerboardNeedsRefresh)
        {
            UpdateCheckerboard(canvasWidth, canvasHeight);
            _checkerboardNeedsRefresh = false;
        }

        XAxis.Width = canvasWidth;
        Canvas.SetLeft(XAxis, 0);
        Canvas.SetTop(XAxis, axisY - (XAxis.Height / 2.0));

        YAxis.Height = canvasHeight;
        Canvas.SetLeft(YAxis, axisX - (YAxis.Width / 2.0));
        Canvas.SetTop(YAxis, 0);

        double spriteWidth = Math.Max(1.0, _spriteSourceWidth * _zoom);
        double spriteHeight = Math.Max(1.0, _spriteSourceHeight * _zoom);

        double spriteCanvasX = axisX + (_spritePosition.X * _zoom);
        double spriteCanvasY = axisY - (_spritePosition.Y * _zoom);
        SpriteImage.Width = spriteWidth;
        SpriteImage.Height = spriteHeight;
        Canvas.SetLeft(SpriteImage, spriteCanvasX - (spriteWidth / 2.0));
        Canvas.SetTop(SpriteImage, spriteCanvasY - (spriteHeight / 2.0));

        ZoomTextBlock.Text = $"Zoom: {(int)Math.Round(_zoom * 100)}%";
    }

    private void UpdateCheckerboard(double canvasWidth, double canvasHeight)
    {
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(canvasWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(canvasHeight));
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

        const int tileSize = 8;
        int idx = 0;
        for (int y = 0; y < pixelHeight; y++)
        {
            int tileY = y / tileSize;
            for (int x = 0; x < pixelWidth; x++)
            {
                int tileX = x / tileSize;
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
    private void CoordinateCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _checkerboardNeedsRefresh = true;
        UpdateVisuals();
    }

    private void CoordinateCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CoordinateCanvas);
        _dragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _dragStartPan = _pan;
        _isDragging = true;
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

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);

        _pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        UpdateVisuals();
        e.Handled = true;
    }

    private void CenterOriginButton_Click(object sender, RoutedEventArgs e)
    {
        _pan = Vector2.Zero;
        UpdateVisuals();
    }

    private void VectorXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _spritePosition = new Vector2(double.IsNaN(sender.Value) ? 0 : (float)sender.Value, _spritePosition.Y);
        UpdateVisuals();
        SpritePositionChanged?.Invoke(_spritePosition);
    }

    private void VectorYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _spritePosition = new Vector2(_spritePosition.X, double.IsNaN(sender.Value) ? 0 : (float)sender.Value);
        UpdateVisuals();
        SpritePositionChanged?.Invoke(_spritePosition);
    }
}
