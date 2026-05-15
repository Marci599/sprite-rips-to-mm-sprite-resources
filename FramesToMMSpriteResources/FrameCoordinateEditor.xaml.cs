using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.WindowsAppSDK.Runtime.Packages;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Globalization.NumberFormatting;

namespace FramesToMMSpriteResources;
//TODO: DRAG ON SCLAED SCREENS GETS MESSED UP

public sealed partial class FrameCoordinateEditor : UserControl
{
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPan;
    private bool _isDragging;
    private bool _isFrameDragging;
    private Vector2 _frameDragStartPointer;
    private IntVector2 _frameDragStartOffset;
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 18.0f;
    private int _selectedFrame;
    private bool _isUpdatingZoomControls;
    private readonly DispatcherTimer _previewTimer = new();
    private IReadOnlyList<SKBitmap> _previewFrames = [];
    private int _previewFrameIndex;
    private int _previewTickCount;
    private SubjectConfig _subjectConfig = new();
    private string? _animationConfigName = null;
    private const float MinPreviewZoom = 0.05f;
    private const float MaxPreviewZoom = 12.0f;
    private bool _isPreviewDragging;
    private Vector2 _previewDragStartPointer;
    private Vector2 _previewDragStartPan;
    private ResizeDirection _previewResizeDirection;
    private double _previewResizeStartWidth;
    private double _previewResizeStartHeight;
    private Vector2 _previewResizeStartPointer;
    private readonly InputCursor _resizeLeftCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    private readonly InputCursor _resizeBottomCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    private readonly InputCursor _resizeCornerCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNortheastSouthwest);
    private readonly HashSet<Windows.System.VirtualKey> _heldNudgeKeys = [];
    private readonly DispatcherTimer _nudgeHoldTimer = new();
    private int _nudgeHoldTick;
    private const int NudgeHoldDelayTicks = 10;
    byte lightA, lightB;

    private readonly SKPaint _spritePaint = new() {
        IsAntialias = false,


    };
    private readonly SKPaint _previousFramePaint = new() { IsAntialias = false };
    private readonly SKPaint _checkerboardPaint = new() { IsAntialias = false };
    private SKShader? _checkerboardShader;
    private readonly SKBitmap _checkerboardUnitBitmap = new(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);

    SKPaint _xAxisPaint = new() { IsAntialias = false, StrokeWidth = 1f };
    SKPaint _yAxisPaint = new() { IsAntialias = false, StrokeWidth = 1f };
    SKPaint _borderPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f};
    SKPaint _secondaryBorderPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = SKColors.Gray.WithAlpha(170) };


    public event Action<float, float>? RemoveMovementButtonClick;

    private enum ResizeDirection
    {
        None,
        Left,
        Bottom,
        BottomLeft
    }

    public FrameCoordinateEditor()
    {
        InitializeComponent();
        _previousFramePaint.ColorFilter = SKColorFilter.CreateColorMatrix([
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0, 0, 0, 1, 0
        ]);
        SetCheckeredColors();
  
        _previewTimer.Interval = TimeSpan.FromSeconds(1.0 / 60.0);
        _previewTimer.Tick -= PreviewTimer_Tick;
        _previewTimer.Tick += PreviewTimer_Tick;
        _nudgeHoldTimer.Interval = TimeSpan.FromSeconds(1.0 / 60.0);
        _nudgeHoldTimer.Tick -= NudgeHoldTimer_Tick;
        _nudgeHoldTimer.Tick += NudgeHoldTimer_Tick;
    
        ZoomNumberBox.Value = 100;

        //UpdateVisuals();

        ActualThemeChanged -= ThemeChanged;
        ActualThemeChanged += ThemeChanged;


    }

    AnimationConfig getCurrentAnimationConfig()
    {
        return _subjectConfig.AnimationConfigs![_animationConfigName!];
    }

    FrameConfig getCurrentFrameConfig()
    {
        return getCurrentAnimationConfig().FrameCongfigs[_selectedFrame];
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


    public void SetSpriteIndex(int index)
    {
        _selectedFrame = index;
        RefreshOffsetFieldVisually();

        UpdateVisuals();
    }

    public void RefreshOffsetFieldVisually()
    {
        OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;

        OffsetXTextBox.Value = getCurrentFrameConfig().Offset.X;
        OffsetYTextBox.Value = getCurrentFrameConfig().Offset.Y;

        OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
        OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;
    }

    void UnsubscribeCanvases()
    {
        CoordinateCanvas.PaintSurface -= CoordinateCanvas_PaintSurface;
        CoordinateCanvas.SizeChanged -= CoordinateCanvas_SizeChanged;
        CoordinateCanvas.PointerPressed -= CoordinateCanvas_PointerPressed;
        CoordinateCanvas.PointerMoved -= CoordinateCanvas_PointerMoved;
        CoordinateCanvas.PointerReleased -= CoordinateCanvas_PointerReleased;
        CoordinateCanvas.PointerWheelChanged -= CoordinateCanvas_PointerWheelChanged;

        AnimationPreviewCanvas.PaintSurface -= AnimationPreviewCanvas_PaintSurface;
        AnimationPreviewCanvas.SizeChanged -= AnimationPreviewCanvas_SizeChanged;
        AnimationPreviewCanvas.PointerPressed -= AnimationPreviewCanvas_PointerPressed;
        AnimationPreviewCanvas.PointerMoved -= AnimationPreviewCanvas_PointerMoved;
        AnimationPreviewCanvas.PointerReleased -= AnimationPreviewCanvas_PointerReleased;
        AnimationPreviewCanvas.PointerWheelChanged -= AnimationPreviewCanvas_PointerWheelChanged;

        ShowPreviousToggleSwitch.Toggled -= ShowPreviousToggleSwitch_Toggled;
        ZoomNumberBox.ValueChanged -= ZoomNumberBox_ValueChanged;

        FromNumberBox.ValueChanged -= FromNumberBox_ValueChanged;
        ToNumberBox.ValueChanged -= ToNumberBox_ValueChanged;

        DirectionNumberBox.ValueChanged -= DirectionNumberBox_ValueChanged;
        SpeedNumberBox.ValueChanged -= SpeedNumberBox_ValueChanged;

        BasedOnRadioButtons.SelectionChanged -= BasedOnRadioButtons_SelectionChanged;
    }

    private void BasedOnRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        getCurrentAnimationConfig().AlignBasedOn = (AlignBasedOn)(sender as RadioButtons)!.SelectedIndex;
    }

    private void DirectionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        getCurrentAnimationConfig().Direction = double.IsNaN(sender.Value) ? 90 : (float)sender.Value;
    }

    private void SpeedNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        getCurrentAnimationConfig().Speed = double.IsNaN(sender.Value) ? 0 : (float)sender.Value;
    }

    public void LoadAnimation(IReadOnlyList<SKBitmap> frames, SubjectConfig subjectConfig, string? animationConfigName, SKColor? backgroundSKColor)
    {
        UnsubscribeCanvases();
      

        _previewFrames = frames;
  
 
        _previewFrameIndex = 0;
        _previewTickCount = 0;
        _subjectConfig = subjectConfig;
        _animationConfigName = animationConfigName;



        CoordinateCanvas.PaintSurface += CoordinateCanvas_PaintSurface;
        AnimationPreviewCanvas.PaintSurface += AnimationPreviewCanvas_PaintSurface;

        CoordinateCanvas.SizeChanged += CoordinateCanvas_SizeChanged;
        AnimationPreviewCanvas.SizeChanged += AnimationPreviewCanvas_SizeChanged;

        if (_previewFrames.Count > 0)
        {
            int maxFrames = Math.Max(_previewFrames.Count - 1, 0);
            ToNumberBox.PlaceholderText = maxFrames.ToString();
            ToNumberBox.Maximum = maxFrames;
            FromNumberBox.Maximum = maxFrames;
            _previewTimer.Start();

            ShowPreviousToggleSwitch.IsOn = MainWindow.ProgramConfig.ShowPreviousFrameBehind;
            ZoomNumberBox.Value = (double)(_subjectConfig.EditorCanvas.Zoom * 100);

            DirectionNumberBox.Value = getCurrentAnimationConfig().Direction;
            SpeedNumberBox.Value = getCurrentAnimationConfig().Speed;

            FromNumberBox.Value = getCurrentAnimationConfig().Range.From;
            if(getCurrentAnimationConfig().Range.To != -1)
            {
                ToNumberBox.Value = getCurrentAnimationConfig().Range.To;
            }
            else
            {
                ToNumberBox.Text = null;
            }

            BasedOnRadioButtons.SelectedIndex = (int)getCurrentAnimationConfig().AlignBasedOn;

            if(_subjectConfig.PreviewSize != null)
            {
                AnimationPreviewHostBorder.Width = _subjectConfig.PreviewSize.Value.X;
                AnimationPreviewHostBorder.Height = _subjectConfig.PreviewSize.Value.Y;
            }
 


            CoordinateCanvas.PointerPressed += CoordinateCanvas_PointerPressed;
            CoordinateCanvas.PointerMoved += CoordinateCanvas_PointerMoved;
            CoordinateCanvas.PointerReleased += CoordinateCanvas_PointerReleased;
            CoordinateCanvas.PointerWheelChanged += CoordinateCanvas_PointerWheelChanged;


            AnimationPreviewCanvas.PointerPressed += AnimationPreviewCanvas_PointerPressed;
            AnimationPreviewCanvas.PointerMoved += AnimationPreviewCanvas_PointerMoved;
            AnimationPreviewCanvas.PointerReleased += AnimationPreviewCanvas_PointerReleased;
            AnimationPreviewCanvas.PointerWheelChanged += AnimationPreviewCanvas_PointerWheelChanged;

            ShowPreviousToggleSwitch.Toggled += ShowPreviousToggleSwitch_Toggled;
            ZoomNumberBox.ValueChanged += ZoomNumberBox_ValueChanged;

            FromNumberBox.ValueChanged += FromNumberBox_ValueChanged;
            ToNumberBox.ValueChanged += ToNumberBox_ValueChanged;

            DirectionNumberBox.ValueChanged += DirectionNumberBox_ValueChanged;
            SpeedNumberBox.ValueChanged += SpeedNumberBox_ValueChanged;

            BasedOnRadioButtons.SelectionChanged += BasedOnRadioButtons_SelectionChanged;


            SKColor backgroundSKColorNotNull = (SKColor)backgroundSKColor!;


            if (backgroundSKColorNotNull.Alpha != 0)
            {
                
                _xAxisPaint.Color = ColorHelper.RotateSkColor(backgroundSKColorNotNull, -90).WithAlpha(170);
                _yAxisPaint.Color = ColorHelper.RotateSkColor(backgroundSKColorNotNull, 90).WithAlpha(170);
                _borderPaint.Color = ColorHelper.RotateSkColor(backgroundSKColorNotNull, 180).WithAlpha(170);

            }
            else
            {
                SKColor gray = new(127, 127, 127, 170);
                _xAxisPaint.Color = gray;
                _yAxisPaint.Color = gray;
                _borderPaint.Color = gray;
            }
        }
        else
        {
            _previewTimer.Stop();
        }

        UpdateAnimationPreviewFrame();
    }

    public void UnloadAnimation()
    {     
        LoadAnimation([], _subjectConfig, _animationConfigName, null);
        UpdateVisuals();
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
            _frameDragStartOffset = getCurrentFrameConfig().Offset;
            _isFrameDragging = true;
            CoordinateCanvas.CapturePointer(e.Pointer);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            _dragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
            _dragStartPan = _subjectConfig.EditorCanvas.Pan;
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
            _subjectConfig.EditorCanvas.Pan = _dragStartPan + (currentPosition - _dragStartPointer);
            UpdateVisuals();
        }
        else if (_isFrameDragging)
        {
            var delta = currentPosition - _frameDragStartPointer;
            int dx = (int)MathF.Round(delta.X / _subjectConfig.EditorCanvas.Zoom);
            int dy = (int)MathF.Round(-delta.Y / _subjectConfig.EditorCanvas.Zoom);
            IntVector2 newOffset = new(_frameDragStartOffset.X + dx, _frameDragStartOffset.Y + dy);
            IntVector2 currentOffset = getCurrentFrameConfig().Offset;
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

        float oldZoom = _subjectConfig.EditorCanvas.Zoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = CoordinateCanvas.ActualWidth / 2.0;
        double centerY = CoordinateCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + _subjectConfig.EditorCanvas.Pan.X;
        double oldAxisY = centerY + _subjectConfig.EditorCanvas.Pan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        _subjectConfig.EditorCanvas.Zoom = newZoom;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);

        _subjectConfig.EditorCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        if (_isDragging)
        {
            _dragStartPan = _subjectConfig.EditorCanvas.Pan;
            _dragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        }
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
        double zoomPercent = Math.Round(_subjectConfig.EditorCanvas.Zoom * 100);
      
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
        if (Math.Abs(newZoom - _subjectConfig.EditorCanvas.Zoom) < 0.0001f)
        {
            return;
        }

        float oldZoom = _subjectConfig.EditorCanvas.Zoom;

        _subjectConfig.EditorCanvas.Zoom = newZoom;

        double centerX = CoordinateCanvas.ActualWidth / 2.0;
        double centerY = CoordinateCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + _subjectConfig.EditorCanvas.Pan.X;
        double oldAxisY = centerY + _subjectConfig.EditorCanvas.Pan.Y;

        double worldX = (centerX - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - centerY) / oldZoom;

        double newAxisX = centerX - (worldX * newZoom);
        double newAxisY = centerY + (worldY * newZoom);

        _subjectConfig.EditorCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        UpdateVisuals();
    }

    private void CenterOriginButton_Click(object sender, RoutedEventArgs e)
    {
        _subjectConfig.EditorCanvas.Pan = Vector2.Zero;
        _subjectConfig.PreviewCanvas.Pan = Vector2.Zero;
        
        UpdateAnimationPreviewFrame();
        UpdateVisuals();
    }

    private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {

        SpritePositionChanged?.Invoke(new(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, getCurrentFrameConfig().Offset.Y));
        UpdateVisuals();

    }

    private void OffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        SpritePositionChanged?.Invoke(new IntVector2(getCurrentFrameConfig().Offset.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value));
        UpdateVisuals();

    }

    private void ALignDownButton_Click(object sender, RoutedEventArgs e)
    {
        SpritePositionChanged?.Invoke(new(0, GetCurrentFrame()?.Height / 2 ?? 0));
        RefreshOffsetFieldVisually();
        UpdateVisuals();
    }

    private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
    {
        SpritePositionChanged?.Invoke(new(GetCurrentFrame()?.Width / 2 ?? 0, (GetCurrentFrame()?.Height / 2 ?? 0)*-1));
        RefreshOffsetFieldVisually();
        UpdateVisuals();
    }

    private void ALignCenterButton_Click(object sender, RoutedEventArgs e)
    {
        SpritePositionChanged?.Invoke(new(0, 0));
        RefreshOffsetFieldVisually();
        UpdateVisuals();
    }

    public void NudgeOffset(int dx, int dy)
    {
        if ((dx == 0 && dy == 0) || getCurrentAnimationConfig().FrameCongfigs == null)
        {
            return;
        }
      
        SpritePositionMoved?.Invoke(new(dx, dy));
        RefreshOffsetFieldVisually();
        UpdateVisuals();
      
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
        //SetMaxPreviewSize();
    }

    void SetMaxPreviewSize()
    {
        var padding = (AnimationPreviewHostBorder.Parent as StackPanel)!.Margin.Right * 2;
        AnimationPreviewHostBorder.MaxHeight = CoordinateCanvas.ActualHeight - padding - FrameRangeBorder.ActualHeight;
        AnimationPreviewHostBorder.MaxWidth = CoordinateCanvas.ActualWidth - padding;
    }

    private void AnimationPreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_previewResizeDirection != ResizeDirection.None)
        {
            return;
        }
  
        var point = e.GetCurrentPoint(AnimationPreviewCanvas);

        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        _previewDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _previewDragStartPan = _subjectConfig.PreviewCanvas.Pan;
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
        _subjectConfig.PreviewCanvas.Pan = _previewDragStartPan + (currentPosition - _previewDragStartPointer);
        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    private void AnimationPreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPreviewDragging = false;
        AnimationPreviewCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void PreviewResizeLeftHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartPreviewResize(sender, e, ResizeDirection.Left);
    }
    private void PreviewResizeLeftHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = _resizeLeftCursor;
    }

    private void PreviewResizeBottomHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartPreviewResize(sender, e, ResizeDirection.Bottom);
    }
    private void PreviewResizeBottomHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = _resizeBottomCursor;
    }

    private void PreviewResizeCornerHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartPreviewResize(sender, e, ResizeDirection.BottomLeft);
    }
    private void PreviewResizeCornerHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = _resizeCornerCursor;
    }

    private void PreviewResizeHandle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_previewResizeDirection == ResizeDirection.None)
        {
            ProtectedCursor = null;
        }
    }

    private void StartPreviewResize(object sender, PointerRoutedEventArgs e, ResizeDirection direction)
    {
        if (sender is not UIElement handle)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        _previewResizeDirection = direction;
        _previewResizeStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        _previewResizeStartWidth = AnimationPreviewHostBorder.ActualWidth;
        _previewResizeStartHeight = AnimationPreviewHostBorder.ActualHeight;
        handle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PreviewResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_previewResizeDirection == ResizeDirection.None)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var delta = new Vector2((float)point.Position.X, (float)point.Position.Y) - _previewResizeStartPointer;
        double width = _previewResizeStartWidth;
        double height = _previewResizeStartHeight;

        if (_previewResizeDirection is ResizeDirection.Left or ResizeDirection.BottomLeft)
        {
            width = Math.Max(0, _previewResizeStartWidth - delta.X);          
        }

        if (_previewResizeDirection is ResizeDirection.Bottom or ResizeDirection.BottomLeft)
        {
            height = Math.Max(0, _previewResizeStartHeight + delta.Y);        
        }

        _subjectConfig.PreviewSize = new((float)width, (float)height);
        AnimationPreviewHostBorder.Width = width;
        AnimationPreviewHostBorder.Height = height;
        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    private void PreviewResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement handle)
        {
            handle.ReleasePointerCapture(e.Pointer);
        }

        _previewResizeDirection = ResizeDirection.None;
        ProtectedCursor = null;
        e.Handled = true;
    }

    private void AnimationPreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(AnimationPreviewCanvas);
        int wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        float oldZoom = _subjectConfig.PreviewCanvas.Zoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinPreviewZoom, MaxPreviewZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = AnimationPreviewCanvas.ActualWidth / 2.0;
        double centerY = AnimationPreviewCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + _subjectConfig.PreviewCanvas.Pan.X;
        double oldAxisY = centerY + _subjectConfig.PreviewCanvas.Pan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        _subjectConfig.PreviewCanvas.Zoom = newZoom;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);
        _subjectConfig.PreviewCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        if (_isPreviewDragging)
        {
            _previewDragStartPan = _subjectConfig.PreviewCanvas.Pan;
            _previewDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        }

        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    int GetCorrectRangeToValue()
    {
        if(getCurrentAnimationConfig().Range.To == -1)
        {
            return Math.Max(_previewFrames.Count - 1, 0);
        }

        return getCurrentAnimationConfig().Range.To;
    }

    private void PreviewTimer_Tick(object? sender, object e)
    {
        if (_previewFrames.Count == 0)
        {
            _previewTimer.Stop();
            return;
        }

        _previewTickCount++;
        if (_previewTickCount < getCurrentAnimationConfig().Delay)
        {
            return;
        }

        _previewTickCount = 0;
        _previewFrameIndex = (_previewFrameIndex + 1) % (GetCorrectRangeToValue() + 1 - getCurrentAnimationConfig().Range.From);
        UpdateAnimationPreviewFrame();
    }

    private void UpdateAnimationPreviewFrame()
    {
        AnimationPreviewCanvas.Invalidate();
    }

    private SKBitmap? GetCurrentFrame() => (_previewFrames.Count == 0 || _selectedFrame >= _previewFrames.Count) ? null : _previewFrames[_selectedFrame];




    private void CoordinateCanvas_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        int width = e.Info.Width;
        int height = e.Info.Height;

        canvas.Clear();

        float zoom = _subjectConfig.EditorCanvas.Zoom;
        var pan = _subjectConfig.EditorCanvas.Pan;
        float axisX = width / 2f + pan.X;
        float axisY = height / 2f + pan.Y;

        DrawCheckerboard(canvas, width, height, axisX, axisY, zoom);

        if (_previewFrames.Count == 0)
        {
            return;
        }

        if (MainWindow.ProgramConfig.ShowPreviousFrameBehind)
        {
            int previousFrameIndex = _selectedFrame == 0 ? _previewFrames.Count - 1 : _selectedFrame - 1;
            SKBitmap? previousFrame = _previewFrames[previousFrameIndex];
            if (previousFrame != null)
            {
                var destRect = DrawFrame(canvas, previousFrame, getCurrentAnimationConfig().FrameCongfigs[previousFrameIndex].Offset, zoom, axisX, axisY, width, height, 0.5f, _previousFramePaint);

                canvas.DrawRect(destRect, _secondaryBorderPaint);
            }
        }

        SKBitmap? currentFrame = GetCurrentFrame();
        if (currentFrame != null)
        {
            var destRect = DrawFrame(canvas, currentFrame, getCurrentFrameConfig().Offset, zoom, axisX, axisY, width, height, MainWindow.ProgramConfig.ShowPreviousFrameBehind ? 0.5f : 1f);
            
            canvas.DrawRect(destRect, _borderPaint);
       

        }

  

        canvas.DrawLine(0f, axisY, width, axisY, _xAxisPaint);

        canvas.DrawLine(axisX, 0f, axisX, height, _yAxisPaint);    
    }

    private void AnimationPreviewCanvas_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        int width = e.Info.Width;
        int height = e.Info.Height;

        canvas.Clear();

        float zoom = _subjectConfig.PreviewCanvas.Zoom;
        var pan = _subjectConfig.PreviewCanvas.Pan;
        float axisX = width / 2f + pan.X;
        float axisY = height / 2f + pan.Y;

        DrawCheckerboard(canvas, width, height, axisX, axisY, zoom);

        if (_previewFrames.Count == 0)
        {
            return;
        }

        int previewFrameIndex = Math.Clamp(_previewFrameIndex + getCurrentAnimationConfig().Range.From, getCurrentAnimationConfig().Range.From, GetCorrectRangeToValue());
        SKBitmap? previewFrame = _previewFrames[previewFrameIndex];
        if (previewFrame != null)
        {
            IntVector2 previewOffset = previewFrameIndex < getCurrentAnimationConfig().FrameCongfigs.Count
                ? getCurrentAnimationConfig().FrameCongfigs[previewFrameIndex].Offset
                : new IntVector2();
            DrawFrame(canvas, previewFrame, previewOffset, zoom, axisX, axisY, width, height, 1f);
        }




        canvas.DrawLine(0f, axisY, width, axisY, _xAxisPaint);

        canvas.DrawLine(axisX, 0f, axisX, height, _yAxisPaint);
    }



    private void DrawCheckerboard(SKCanvas canvas, int width, int height, float axisX, float axisY, float zoom)
    {
        float tileSize = Math.Max(1f, 4f * zoom);
        _checkerboardPaint.Shader = _checkerboardShader;
        canvas.Save();
        canvas.Translate(axisX, axisY);
        canvas.Scale(tileSize, tileSize);
        canvas.DrawRect(
            new SKRect(-axisX / tileSize, -axisY / tileSize, (width - axisX) / tileSize, (height - axisY) / tileSize),
            _checkerboardPaint);
        canvas.Restore();
    }

    private SKRect DrawFrame(SKCanvas canvas, SKBitmap bitmap, IntVector2 offset, float zoom, float axisX, float axisY, int viewportWidth, int viewportHeight, float alpha, SKPaint? overridePaint = null)
    {
        float width = Math.Max(1f, bitmap.Width * zoom);
        float height = Math.Max(1f, bitmap.Height * zoom);
        float x = axisX + (offset.X * zoom) - (width / 2f);
        float y = axisY - (offset.Y * zoom) - (height / 2f);
        SKRect destRect = new(x, y, x + width, y + height);
        SKRect viewportRect = new(0, 0, viewportWidth, viewportHeight);
        if (!destRect.IntersectsWith(viewportRect))
        {
            return destRect;
        }

        SKRect clippedDest = SKRect.Intersect(destRect, viewportRect);
        float invScaleX = bitmap.Width / width;
        float invScaleY = bitmap.Height / height;
        SKRect sourceRect = new(
            (clippedDest.Left - destRect.Left) * invScaleX,
            (clippedDest.Top - destRect.Top) * invScaleY,
            (clippedDest.Right - destRect.Left) * invScaleX,
            (clippedDest.Bottom - destRect.Top) * invScaleY);

        SKPaint paint = overridePaint ?? _spritePaint;

        paint.Color = new SKColor(255, 255, 255, (byte)(alpha * 255f));
 
        canvas.DrawBitmap(bitmap, sourceRect, clippedDest, paint);

        return destRect;
        
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
        MainWindow.ProgramConfig.ShowPreviousFrameBehind = (sender as ToggleSwitch)!.IsOn;
        UpdateVisuals();
    }

 

    private void FromNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        getCurrentAnimationConfig().Range.From = double.IsNaN(FromNumberBox.Value) ? 0 : (int)FromNumberBox.Value;
        ToNumberBox.Minimum = getCurrentAnimationConfig().Range.From;
    }

    private void ToNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        var number = ToNumberBox.Value;
        var max = Math.Max(_previewFrames.Count - 1, 0);
        if (double.IsNaN(number) || number == max)
        {
            getCurrentAnimationConfig().Range.To = -1;
            FromNumberBox.Maximum = max;
            return;
        }

        getCurrentAnimationConfig().Range.To = (int)number;
        FromNumberBox.Maximum = number;
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

    public void EnableMovementControls(bool isEnabled)
    {
   
        RemoveMovementButton.IsEnabled = isEnabled;
    }

    private void RemoveMovementButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveMovementButtonClick?.Invoke(getCurrentAnimationConfig().Direction, getCurrentAnimationConfig().Speed);
        
        UpdateVisuals();      
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
