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
using System.Runtime.InteropServices;
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
    private WriteableBitmap? _editorCompositeBitmap;
    private byte[]? _editorPixels;
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

        _editorCompositeBitmap = null;
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
        EditorCompositeImage.Source = null;
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
        RenderEditorComposite((int)Math.Ceiling(canvasWidth), (int)Math.Ceiling(canvasHeight), axisX, axisY);
        UpdateZoomControls();
    }

    private void RenderEditorComposite(int pixelWidth, int pixelHeight, double axisX, double axisY)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        int byteCount = pixelWidth * pixelHeight * 4;
        if (_editorCompositeBitmap == null || _editorCompositeBitmap.PixelWidth != pixelWidth || _editorCompositeBitmap.PixelHeight != pixelHeight)
        {
            _editorCompositeBitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _editorPixels = new byte[byteCount];
            EditorCompositeImage.Source = _editorCompositeBitmap;
            EditorCompositeImage.Width = pixelWidth;
            EditorCompositeImage.Height = pixelHeight;
            Canvas.SetLeft(EditorCompositeImage, 0);
            Canvas.SetTop(EditorCompositeImage, 0);
        }
        else if (_editorPixels == null || _editorPixels.Length != byteCount)
        {
            _editorPixels = new byte[byteCount];
        }
        Span<byte> pixels = _editorPixels!;
        double tileSize = 4.0 * _zoom;
        int idx = 0;
        for (int y = 0; y < pixelHeight; y++)
        {
            int tileY = (int)Math.Floor((axisY - y) / tileSize);
            for (int x = 0; x < pixelWidth; x++)
            {
                int tileX = (int)Math.Floor((x - axisX) / tileSize);
                byte color = ((tileX + tileY) & 1) == 0 ? lightA : lightB;
                pixels[idx++] = color; pixels[idx++] = color; pixels[idx++] = color; pixels[idx++] = 255;
            }
        }
        int axisXi = (int)Math.Round(axisX);
        int axisYi = (int)Math.Round(axisY);
        if (axisXi >= 0 && axisXi < pixelWidth)
        {
            for (int y = 0; y < pixelHeight; y++)
            {
                int i = (y * pixelWidth + axisXi) * 4;
                pixels[i] = 180; pixels[i + 1] = 180; pixels[i + 2] = 180; pixels[i + 3] = 255;
            }
        }
        if (axisYi >= 0 && axisYi < pixelHeight)
        {
            for (int x = 0; x < pixelWidth; x++)
            {
                int i = (axisYi * pixelWidth + x) * 4;
                pixels[i] = 180; pixels[i + 1] = 180; pixels[i + 2] = 180; pixels[i + 3] = 255;
            }
        }

        if (_previewFrames.Count > 0 && _selectedFrame < _previewFrames.Count)
        {
            DrawSpriteToBuffer(_previewFrames[_selectedFrame], _animationConfig.frameCongfigs[_selectedFrame].Offset, axisX, axisY, pixels, pixelWidth, pixelHeight, false, 255);
            if (ShowPreviousToggleSwitch.IsOn)
            {
                int prev = _selectedFrame == 0 ? _previewFrames.Count - 1 : _selectedFrame - 1;
                DrawSpriteToBuffer(_previewFrames[prev], _animationConfig.frameCongfigs[prev].Offset, axisX, axisY, pixels, pixelWidth, pixelHeight, true, 140);
            }
        }
        using Stream stream = _editorCompositeBitmap!.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(_editorPixels!, 0, _editorPixels!.Length);
        _editorCompositeBitmap.Invalidate();
    }

    private void DrawSpriteToBuffer(WriteableBitmap bitmap, IntVector2 offset, double axisX, double axisY, Span<byte> target, int w, int h, bool grayscale, byte alpha)
    {
        byte[] source = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        using (Stream sourceStream = bitmap.PixelBuffer.AsStream())
        {
            sourceStream.Position = 0;
            _ = sourceStream.Read(source, 0, source.Length);
        }
        int scale = Math.Max(1, (int)Math.Round(_zoom));
        int left = (int)Math.Round(axisX + (offset.X * _zoom) - ((bitmap.PixelWidth * _zoom) / 2.0));
        int top = (int)Math.Round(axisY - (offset.Y * _zoom) - ((bitmap.PixelHeight * _zoom) / 2.0));
        for (int y = 0; y < bitmap.PixelHeight; y++)
        for (int x = 0; x < bitmap.PixelWidth; x++)
        {
            int si = (y * bitmap.PixelWidth + x) * 4;
            byte b = source[si], g = source[si + 1], r = source[si + 2], a = source[si + 3];
            if (a == 0) continue;
            if (grayscale) { byte gv = (byte)((r * 77 + g * 150 + b * 29) >> 8); r = g = b = gv; }
            int dstX = left + (int)Math.Round(x * _zoom);
            int dstY = top + (int)Math.Round(y * _zoom);
            for (int yy = 0; yy < scale; yy++)
            for (int xx = 0; xx < scale; xx++)
            {
                int tx = dstX + xx, ty = dstY + yy;
                if ((uint)tx >= w || (uint)ty >= h) continue;
                int di = (ty * w + tx) * 4;
                target[di] = b; target[di + 1] = g; target[di + 2] = r; target[di + 3] = Math.Min(alpha, a);
            }
        }
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
        OffsetYTextBox.Value = _previewFrames[_selectedFrame].PixelHeight / 2;
    }

    private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
    {
        OffsetXTextBox.Value = (_previewFrames[_selectedFrame].PixelWidth / 2);
        OffsetYTextBox.Value = -(_previewFrames[_selectedFrame].PixelHeight / 2);
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
