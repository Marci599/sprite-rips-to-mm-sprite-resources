using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Globalization.NumberFormatting;

namespace FramesToMMSpriteResources;

public sealed partial class FrameCoordinateEditor : UserControl
{
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPan;
    private bool _isDragging;
    private bool _isFrameDragging;
    private Vector2 _frameDragStartPointer;
    private IntVector2 _frameDragStartOffset;
    private float _zoom = 1.0f;
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 18.0f;
    private WriteableBitmap? _checkerBitmap;
    private byte[]? _checkerPixels;
    private int _selectedFrame;
    private bool _isUpdatingZoomControls;
    private readonly DispatcherTimer _previewTimer = new();
    private IReadOnlyList<WriteableBitmap> _previewFrames = [];
    private int _previewFrameIndex;
    private int _previewTickCount;
    private AnimationConfig _animationConfig = new();
    private Vector2 _previewPan = Vector2.Zero;
    private float _previewZoom = 1.0f;
    private const float MinPreviewZoom = 0.05f;
    private const float MaxPreviewZoom = 12.0f;
    private bool _isPreviewDragging;
    private Vector2 _previewDragStartPointer;
    private Vector2 _previewDragStartPan;
    private readonly HashSet<Windows.System.VirtualKey> _heldNudgeKeys = [];
    byte lightA, lightB;

    public FrameCoordinateEditor()
    {
        InitializeComponent();
        SetCheckeredColors();
        _previewTimer.Interval = TimeSpan.FromSeconds(1.0 / 60.0);
        _previewTimer.Tick -= PreviewTimer_Tick;
        _previewTimer.Tick += PreviewTimer_Tick;
    
        ZoomNumberBox.Value = 100;

        UpdateVisuals();

        ActualThemeChanged -= ThemeChanged;
        ActualThemeChanged += ThemeChanged;
    }

    void ThemeChanged(FrameworkElement fe, object o)
    {
        SetCheckeredColors();

        _checkerBitmap = null;
        UpdateVisuals();
    }

    void SetCheckeredColors()
    {
        bool isLight = ActualTheme == ElementTheme.Light;

        if (isLight)
        {
            lightA = 220;
            lightB = 255;
        }
        else
        {
            lightA = 30;
            lightB = 60;
        }
    }

    public event Action<IntVector2>? SpritePositionChanged;

    public event Action<IntVector2>? SpritePositionMoved;

    int GetFromValue()
    {
        return double.IsNaN(FromNumberBox.Value) ? 0 : (int)FromNumberBox.Value;
    }

    int GetToValue()
    {
        return double.IsNaN(ToNumberBox.Value) ? Math.Max(_previewFrames.Count - 1, 0) : (int)ToNumberBox.Value;
    }

    public void SetSpriteIndex(int index)
    {
        SpriteImage.Source = _previewFrames[index];
        _selectedFrame = index;

        OffsetXTextBox.Value = _animationConfig.frameCongfigs[index].Offset.X;
        OffsetYTextBox.Value = _animationConfig.frameCongfigs[index].Offset.Y;
        OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
        OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;

        OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;

        if (index == 0)
        {
            index = _previewFrames.Count;
        }
        SpriteBeforeImage.Source = _previewFrames[index - 1];
        UpdateVisuals();
    }

    public void LoadAnimation(IReadOnlyList<WriteableBitmap> frames, AnimationConfig animationConfig)
    {
        _previewFrames = frames;
        _previewFrameIndex = 0;
        _previewTickCount = 0;
        _animationConfig = animationConfig;
        int maxFrames = Math.Max(_previewFrames.Count - 1, 0);
        ToNumberBox.PlaceholderText = maxFrames.ToString();
        ToNumberBox.Maximum = maxFrames;
        FromNumberBox.Maximum = maxFrames;

        UpdateAnimationPreviewFrame();

        if (_previewFrames.Count > 0)
        {
            _previewTimer.Start();
        }
        else
        {
            _previewTimer.Stop();
        }
    }

    public void UnloadAnimation()
    {
        SpriteImage.Source = null;
        SpriteBeforeImage.Source = null;
        LoadAnimation([], new());
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


        if (SpriteImage.Source == null) return;

        double spriteWidth = Math.Max(1.0, (SpriteImage.Source as WriteableBitmap)!.PixelWidth * _zoom);
        double spriteHeight = Math.Max(1.0, (SpriteImage.Source as WriteableBitmap)!.PixelHeight * _zoom);

        double spriteCanvasX = axisX + (_animationConfig.frameCongfigs[_selectedFrame].Offset.X * _zoom);
        double spriteCanvasY = axisY - (_animationConfig.frameCongfigs[_selectedFrame].Offset.Y * _zoom);

        int index = _selectedFrame;
        if (index == 0)
        {
            index = _previewFrames.Count;
        }

        double spriteBeforeCanvasX = axisX + (_animationConfig.frameCongfigs[index -1].Offset.X * _zoom);
        double spriteBeforeCanvasY = axisY - (_animationConfig.frameCongfigs[index -1].Offset.Y * _zoom);

        SpriteImage.Width = spriteWidth;
        SpriteImage.Height = spriteHeight;

        SpriteBeforeImage.Width = spriteWidth;
        SpriteBeforeImage.Height = spriteHeight;

        Canvas.SetLeft(SpriteImage, spriteCanvasX - (spriteWidth / 2.0));
        Canvas.SetTop(SpriteImage, spriteCanvasY - (spriteHeight / 2.0));

        Canvas.SetLeft(SpriteBeforeImage, spriteBeforeCanvasX - (spriteWidth / 2.0));
        Canvas.SetTop(SpriteBeforeImage, spriteBeforeCanvasY - (spriteHeight / 2.0));

        UpdateZoomControls();
    }

    private void UpdateCheckerboard(double canvasWidth, double canvasHeight, double axisX, double axisY)
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

        double tileSize = 4.0 * _zoom;
        int idx = 0;
        for (int y = 0; y < pixelHeight; y++)
        {
  
            int tileY = (int)Math.Floor((axisY - y) / tileSize);
            for (int x = 0; x < pixelWidth; x++)
            {
      
                int tileX = (int)Math.Floor((x - axisX) / tileSize);
                byte color = ((tileX + tileY) & 1) == 0 ? lightA : lightB;

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
        UpdateVisuals();
    }

    private void CoordinateCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
        var point = e.GetCurrentPoint(CoordinateCanvas);
        if (point.Properties.IsLeftButtonPressed)
        {
            _frameDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
            _frameDragStartOffset = _animationConfig.frameCongfigs[_selectedFrame].Offset;
            _isFrameDragging = true;
            CoordinateCanvas.CapturePointer(e.Pointer);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            _dragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
            _dragStartPan = _pan;
            _isDragging = true;
            CoordinateCanvas.CapturePointer(e.Pointer);
        }
    }

    private void CoordinateCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging && !_isFrameDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(CoordinateCanvas);
        var currentPosition = new Vector2((float)point.Position.X, (float)point.Position.Y);
        if (_isDragging)
        {
            _pan = _dragStartPan + (currentPosition - _dragStartPointer);
            UpdateVisuals();
        }
        else if (_isFrameDragging)
        {
            var delta = currentPosition - _frameDragStartPointer;
            int dx = (int)MathF.Round(delta.X / _zoom);
            int dy = (int)MathF.Round(-delta.Y / _zoom);
            IntVector2 newOffset = new(_frameDragStartOffset.X + dx, _frameDragStartOffset.Y + dy);
            IntVector2 currentOffset = _animationConfig.frameCongfigs[_selectedFrame].Offset;
            if (newOffset != currentOffset)
            {
                NudgeOffset(newOffset.X - currentOffset.X, newOffset.Y - currentOffset.Y);
            }
        }
    }

    private void CoordinateCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        _isFrameDragging = false;
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

    private void UpdateZoomControls()
    {
        if (_isUpdatingZoomControls)
        {
            return;
        }

        _isUpdatingZoomControls = true;
        double zoomPercent = Math.Round(_zoom * 100);
      
        ZoomNumberBox.Value = zoomPercent;
        _isUpdatingZoomControls = false;
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
        UpdateVisuals();
    }

    private void CenterOriginButton_Click(object sender, RoutedEventArgs e)
    {
        _pan = Vector2.Zero;
        _previewPan = Vector2.Zero;
        
        UpdateAnimationPreviewFrame();
        UpdateVisuals();
    }

    private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {

        SpritePositionChanged?.Invoke(new(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, _animationConfig.frameCongfigs[_selectedFrame].Offset.Y));
        UpdateVisuals();

    }

    private void OffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        SpritePositionChanged?.Invoke(new IntVector2(_animationConfig.frameCongfigs[_selectedFrame].Offset.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value));
        UpdateVisuals();

    }

    private void ALignDownButton_Click(object sender, RoutedEventArgs e)
    {
        OffsetXTextBox.Value = 0;
        OffsetYTextBox.Value = (SpriteImage.Source as WriteableBitmap)!.PixelHeight / 2;
    }

    private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
    {
        OffsetXTextBox.Value = ((SpriteImage.Source as WriteableBitmap)!.PixelWidth / 2);
        OffsetYTextBox.Value = -((SpriteImage.Source as WriteableBitmap)!.PixelHeight / 2);
    }

    private void ALignCenterButton_Click(object sender, RoutedEventArgs e)
    {
        OffsetXTextBox.Value = 0;
        OffsetYTextBox.Value = 0;
    }

    public void NudgeOffset(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;
        OffsetXTextBox.Value = _animationConfig.frameCongfigs[_selectedFrame].Offset.X + dx;
        OffsetYTextBox.Value = _animationConfig.frameCongfigs[_selectedFrame].Offset.Y + dy;
        SpritePositionMoved?.Invoke(new(dx, dy));
        UpdateVisuals();
        OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;
    }

    public bool HandleNudgeKeyDown(Windows.System.VirtualKey key)
    {
        if (!IsNudgeKey(key) && !IsModifierKey(key))
        {
            return false;
        }

        _heldNudgeKeys.Add(key);
        ApplyHeldNudgeKeys();
        return true;
    }

    public bool HandleNudgeKeyUp(Windows.System.VirtualKey key)
    {
        if (!IsNudgeKey(key) && !IsModifierKey(key))
        {
            return false;
        }

        _heldNudgeKeys.Remove(key);
        ApplyHeldNudgeKeys();
        return true;
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.R)
        {
            ShowPreviousToggleSwitch.IsOn = !ShowPreviousToggleSwitch.IsOn;
            e.Handled = true;
            return;
        }

        if (HandleNudgeKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }

    private void RootGrid_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (HandleNudgeKeyUp(e.Key))
        {
            e.Handled = true;
        }
    }

    private void AnimationPreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAnimationPreviewFrame();
    }

    private void AnimationPreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(AnimationPreviewCanvas);
        _previewDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _previewDragStartPan = _previewPan;
        _isPreviewDragging = true;
        AnimationPreviewCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void AnimationPreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(AnimationPreviewCanvas);
        var currentPosition = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _previewPan = _previewDragStartPan + (currentPosition - _previewDragStartPointer);
        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    private void AnimationPreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPreviewDragging = false;
        AnimationPreviewCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void AnimationPreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(AnimationPreviewCanvas);
        int wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        float oldZoom = _previewZoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinPreviewZoom, MaxPreviewZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = AnimationPreviewCanvas.ActualWidth / 2.0;
        double centerY = AnimationPreviewCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + _previewPan.X;
        double oldAxisY = centerY + _previewPan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        _previewZoom = newZoom;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);
        _previewPan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));

        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    private void PreviewTimer_Tick(object? sender, object e)
    {
        if (_previewFrames.Count == 0)
        {
            _previewTimer.Stop();
            return;
        }

        _previewTickCount++;
        if (_previewTickCount < _animationConfig.Delay)
        {
            return;
        }

        _previewTickCount = 0;
        _previewFrameIndex = (_previewFrameIndex + 1) % (GetToValue() + 1 - GetFromValue());
        UpdateAnimationPreviewFrame();
    }

    private void UpdateAnimationPreviewFrame()
    {
        if (_previewFrames.Count == 0)
        {
            AnimationPreviewImage.Source = null;
            return;
        }

        int frameIndex = Math.Clamp(_previewFrameIndex + GetFromValue(), GetFromValue(), GetToValue());
        WriteableBitmap frame = _previewFrames[frameIndex];
        IntVector2 offset = frameIndex < _animationConfig.frameCongfigs.Count ? _animationConfig.frameCongfigs[frameIndex].Offset : new IntVector2(0, 0);

        double canvasWidth = AnimationPreviewCanvas.ActualWidth;
        double canvasHeight = AnimationPreviewCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        double centerX = canvasWidth / 2.0;
        double centerY = canvasHeight / 2.0;
        double axisX = centerX + _previewPan.X;
        double axisY = centerY + _previewPan.Y;

        PreviewXAxis.Width = canvasWidth;
        Canvas.SetLeft(PreviewXAxis, 0);
        Canvas.SetTop(PreviewXAxis, axisY - (PreviewXAxis.Height / 2.0));

        PreviewYAxis.Height = canvasHeight;
        Canvas.SetLeft(PreviewYAxis, axisX - (PreviewYAxis.Width / 2.0));
        Canvas.SetTop(PreviewYAxis, 0);

        double spriteWidth = Math.Max(1.0, frame.PixelWidth * _previewZoom);
        double spriteHeight = Math.Max(1.0, frame.PixelHeight * _previewZoom);
        double spriteCanvasX = axisX + (offset.X * _previewZoom);
        double spriteCanvasY = axisY - (offset.Y * _previewZoom);

        AnimationPreviewImage.Source = frame;
        AnimationPreviewImage.Width = spriteWidth;
        AnimationPreviewImage.Height = spriteHeight;
        AnimationPreviewImage.RenderTransform = null;
        Canvas.SetLeft(AnimationPreviewImage, spriteCanvasX - (spriteWidth / 2.0));
        Canvas.SetTop(AnimationPreviewImage, spriteCanvasY - (spriteHeight / 2.0));
    }

    private static bool IsNudgeKey(Windows.System.VirtualKey key)
    {
        return key == Windows.System.VirtualKey.W ||
               key == Windows.System.VirtualKey.A ||
               key == Windows.System.VirtualKey.S ||
               key == Windows.System.VirtualKey.D;
    }

    private static bool IsModifierKey(Windows.System.VirtualKey key)
    {
        return key == Windows.System.VirtualKey.Control ||
               key == Windows.System.VirtualKey.LeftControl ||
               key == Windows.System.VirtualKey.RightControl ||
               key == Windows.System.VirtualKey.Shift ||
               key == Windows.System.VirtualKey.LeftShift ||
               key == Windows.System.VirtualKey.RightShift;
    }

    private void ShowPreviousToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if ((sender as ToggleSwitch)!.IsOn)
        {
            SpriteBeforeImage.Visibility = Visibility.Visible;
            SpriteImage.Opacity = 0.7;
        }
        else
        {
            SpriteBeforeImage.Visibility = Visibility.Collapsed;
            SpriteImage.Opacity = 1;
        }
     
    }

    private void FromNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        ToNumberBox.Minimum = GetFromValue();
    }

    private void ToNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        FromNumberBox.Maximum = GetToValue();
    }

    private void ApplyHeldNudgeKeys()
    {
        int dx = (_heldNudgeKeys.Contains(Windows.System.VirtualKey.D) ? 1 : 0) -
                 (_heldNudgeKeys.Contains(Windows.System.VirtualKey.A) ? 1 : 0);
        int dy = (_heldNudgeKeys.Contains(Windows.System.VirtualKey.W) ? 1 : 0) -
                 (_heldNudgeKeys.Contains(Windows.System.VirtualKey.S) ? 1 : 0);

        if (dx == 0 && dy == 0)
        {
            return;
        }

        bool ctrlHeld = _heldNudgeKeys.Contains(Windows.System.VirtualKey.Control) ||
                        _heldNudgeKeys.Contains(Windows.System.VirtualKey.LeftControl) ||
                        _heldNudgeKeys.Contains(Windows.System.VirtualKey.RightControl);
        bool shiftHeld = _heldNudgeKeys.Contains(Windows.System.VirtualKey.Shift) ||
                         _heldNudgeKeys.Contains(Windows.System.VirtualKey.LeftShift) ||
                         _heldNudgeKeys.Contains(Windows.System.VirtualKey.RightShift);

        int multiplier = 1;
        if (ctrlHeld ^ shiftHeld)
        {
            multiplier = 2;
        }
        else if (ctrlHeld && shiftHeld)
        {
            multiplier = 4;
        }

        NudgeOffset(dx * multiplier, dy * multiplier);
    }
}
