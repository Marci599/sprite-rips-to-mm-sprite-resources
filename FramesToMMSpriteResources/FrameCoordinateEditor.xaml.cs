using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Windows;
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
    private readonly DispatcherTimer _nudgeHoldTimer = new();
    private int _nudgeHoldTick;
    private const int NudgeHoldDelayTicks = 10;
    byte lightA, lightB;
    private readonly Dictionary<WriteableBitmap, SKBitmap> _bitmapCache = [];
    private readonly SKPaint _spritePaint = new() { IsAntialias = false };
    private readonly SKPaint _checkerboardPaint = new() { IsAntialias = false };
    private readonly SKPaint _axisPaint = new() { IsAntialias = false, Color = new SKColor(140, 140, 140, 200) };
    private SKShader? _checkerboardShader;
    private readonly SKBitmap _checkerboardUnitBitmap = new(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);

    public FrameCoordinateEditor()
    {
        InitializeComponent();
        SetCheckeredColors();
        _previewTimer.Interval = TimeSpan.FromSeconds(1.0 / 60.0);
        _previewTimer.Tick -= PreviewTimer_Tick;
        _previewTimer.Tick += PreviewTimer_Tick;
        _nudgeHoldTimer.Interval = TimeSpan.FromSeconds(1.0 / 60.0);
        _nudgeHoldTimer.Tick += NudgeHoldTimer_Tick;
    
        ZoomNumberBox.Value = 100;

        UpdateVisuals();

        ActualThemeChanged -= ThemeChanged;
        ActualThemeChanged += ThemeChanged;
    }

    void ThemeChanged(FrameworkElement fe, object o)
    {
        SetCheckeredColors();
        RebuildCheckerboardShader();
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

        RebuildCheckerboardShader();
    }

    private void RebuildCheckerboardShader()
    {
        _checkerboardUnitBitmap.SetPixel(0, 0, new SKColor(lightA, lightA, lightA, 255));
        _checkerboardUnitBitmap.SetPixel(1, 1, new SKColor(lightA, lightA, lightA, 255));
        _checkerboardUnitBitmap.SetPixel(1, 0, new SKColor(lightB, lightB, lightB, 255));
        _checkerboardUnitBitmap.SetPixel(0, 1, new SKColor(lightB, lightB, lightB, 255));
        _checkerboardShader?.Dispose();
        _checkerboardShader = _checkerboardUnitBitmap.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
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
        _selectedFrame = index;

        OffsetXTextBox.Value = _animationConfig.frameCongfigs[index].Offset.X;
        OffsetYTextBox.Value = _animationConfig.frameCongfigs[index].Offset.Y;
        OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
        OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;

        OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;

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
        LoadAnimation([], new());
    }

    private void UpdateVisuals()
    {
        CoordinateCanvas.Invalidate();
        UpdateZoomControls();
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
        OffsetYTextBox.Value = GetCurrentFrame()?.PixelHeight / 2 ?? 0;
    }

    private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
    {
        OffsetXTextBox.Value = (GetCurrentFrame()?.PixelWidth / 2 ?? 0);
        OffsetYTextBox.Value = -(GetCurrentFrame()?.PixelHeight / 2 ?? 0);
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

        bool hadDirection = HasHeldDirectionKey();
        _heldNudgeKeys.Add(key);
        UpdateNudgeMotionState(hadDirection);
        return true;
    }

    public bool HandleNudgeKeyUp(Windows.System.VirtualKey key)
    {
        if (!IsNudgeKey(key) && !IsModifierKey(key))
        {
            return false;
        }

        bool hadDirection = HasHeldDirectionKey();
        _heldNudgeKeys.Remove(key);
        UpdateNudgeMotionState(hadDirection);
        return true;
    }

    private void UpdateNudgeMotionState(bool hadDirectionBeforeChange)
    {
        bool hasDirectionNow = HasHeldDirectionKey();
        if (!hasDirectionNow)
        {
            _nudgeHoldTick = 0;
            _nudgeHoldTimer.Stop();
            return;
        }

        if (!hadDirectionBeforeChange)
        {
            ApplyHeldNudgeKeys();
            _nudgeHoldTick = 0;
            if (!_nudgeHoldTimer.IsEnabled)
            {
                _nudgeHoldTimer.Start();
            }
            return;
        }

        if (!_nudgeHoldTimer.IsEnabled)
        {
            _nudgeHoldTimer.Start();
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.R)
        {
            ToggleShowPreviousFrame();
            e.Handled = true;
            return;
        }

        if (HandleNudgeKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }
    public void ToggleShowPreviousFrame()
    {
        ShowPreviousToggleSwitch.IsOn = !ShowPreviousToggleSwitch.IsOn;
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
        AnimationPreviewCanvas.Invalidate();
    }

    private WriteableBitmap? GetCurrentFrame() => (_previewFrames.Count == 0 || _selectedFrame >= _previewFrames.Count) ? null : _previewFrames[_selectedFrame];

    private SKBitmap? GetSkBitmap(WriteableBitmap? wb)
    {
        if (wb == null) return null;
        if (_bitmapCache.TryGetValue(wb, out var cached)) return cached;
        var bmp = new SKBitmap(wb.PixelWidth, wb.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var stream = wb.PixelBuffer.AsStream();
        var pixels = bmp.GetPixelSpan();
        stream.Position = 0;
        stream.Read(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels));
        _bitmapCache[wb] = bmp;
        return bmp;
    }

    private void CoordinateCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        DrawCanvas(e.Surface.Canvas, e.Info.Width, e.Info.Height, true);
    }

    private void AnimationPreviewCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        DrawCanvas(e.Surface.Canvas, e.Info.Width, e.Info.Height, false);
    }

    private void DrawCanvas(SKCanvas canvas, int width, int height, bool main)
    {
        canvas.Clear();

        float zoom = main ? _zoom : _previewZoom;
        var pan = main ? _pan : _previewPan;
        float axisX = width / 2f + pan.X;
        float axisY = height / 2f + pan.Y;

        DrawCheckerboard(canvas, width, height, axisX, axisY, zoom);

        _axisPaint.StrokeWidth = main ? 1.5f : 1f;
        canvas.DrawLine(0f, axisY, width, axisY, _axisPaint);
        canvas.DrawLine(axisX, 0f, axisX, height, _axisPaint);

        if (_previewFrames.Count == 0)
        {
            return;
        }

        if (main)
        {
            SKBitmap? currentFrame = GetSkBitmap(GetCurrentFrame());
            if (currentFrame != null)
            {
                DrawFrame(canvas, currentFrame, _animationConfig.frameCongfigs[_selectedFrame].Offset, zoom, axisX, axisY, ShowPreviousToggleSwitch.IsOn ? 0.7f : 1f);
            }

            if (ShowPreviousToggleSwitch.IsOn)
            {
                int previousFrameIndex = _selectedFrame == 0 ? _previewFrames.Count - 1 : _selectedFrame - 1;
                SKBitmap? previousFrame = GetSkBitmap(_previewFrames[previousFrameIndex]);
                if (previousFrame != null)
                {
                    DrawFrame(canvas, previousFrame, _animationConfig.frameCongfigs[previousFrameIndex].Offset, zoom, axisX, axisY, 0.5f);
                }
            }
        }
        else
        {
            int previewFrameIndex = Math.Clamp(_previewFrameIndex + GetFromValue(), GetFromValue(), GetToValue());
            SKBitmap? previewFrame = GetSkBitmap(_previewFrames[previewFrameIndex]);
            if (previewFrame != null)
            {
                IntVector2 previewOffset = previewFrameIndex < _animationConfig.frameCongfigs.Count
                    ? _animationConfig.frameCongfigs[previewFrameIndex].Offset
                    : new IntVector2();
                DrawFrame(canvas, previewFrame, previewOffset, zoom, axisX, axisY, 1f);
            }
        }
    }

    private void DrawCheckerboard(SKCanvas canvas, int width, int height, float axisX, float axisY, float zoom)
    {
        _checkerboardShader ??= _checkerboardUnitBitmap.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        float tileSize = Math.Max(1f, 4f * zoom);
        SKMatrix scaleMatrix = SKMatrix.CreateScale(tileSize, tileSize);
        SKMatrix translationMatrix = SKMatrix.CreateTranslation(axisX, axisY);
        SKMatrix shaderMatrix = SKMatrix.Concat(scaleMatrix, translationMatrix);
        _checkerboardPaint.Shader = _checkerboardShader!.WithLocalMatrix(shaderMatrix);
        canvas.DrawRect(new SKRect(0f, 0f, width, height), _checkerboardPaint);
    }

    private void DrawFrame(SKCanvas canvas, SKBitmap bitmap, IntVector2 offset, float zoom, float axisX, float axisY, float alpha)
    {
        float width = Math.Max(1f, bitmap.Width * zoom);
        float height = Math.Max(1f, bitmap.Height * zoom);
        float x = axisX + (offset.X * zoom) - (width / 2f);
        float y = axisY - (offset.Y * zoom) - (height / 2f);

        _spritePaint.Color = new SKColor(255, 255, 255, (byte)(alpha * 255f));
        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height), _spritePaint);
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
        UpdateVisuals();
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

        bool shiftHeld = _heldNudgeKeys.Contains(Windows.System.VirtualKey.Shift) ||
                         _heldNudgeKeys.Contains(Windows.System.VirtualKey.LeftShift) ||
                         _heldNudgeKeys.Contains(Windows.System.VirtualKey.RightShift);

        int multiplier = 1;
        if (shiftHeld)
        {
            multiplier = 2;
        }

        NudgeOffset(dx * multiplier, dy * multiplier);
    }

    private bool HasHeldDirectionKey()
    {
        return _heldNudgeKeys.Contains(Windows.System.VirtualKey.W) ||
               _heldNudgeKeys.Contains(Windows.System.VirtualKey.A) ||
               _heldNudgeKeys.Contains(Windows.System.VirtualKey.S) ||
               _heldNudgeKeys.Contains(Windows.System.VirtualKey.D);
    }

    private void NudgeHoldTimer_Tick(object? sender, object e)
    {
        if (!HasHeldDirectionKey())
        {
            _nudgeHoldTimer.Stop();
            _nudgeHoldTick = 0;
            return;
        }

        _nudgeHoldTick++;
        if (_nudgeHoldTick < NudgeHoldDelayTicks)
        {
            return;
        }

        ApplyHeldNudgeKeys();
    }
}
