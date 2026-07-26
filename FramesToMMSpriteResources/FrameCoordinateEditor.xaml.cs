using FramesToMMSpriteResources.DataConfig;
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
//TODO: DRAG ON SCALED SCREENS GETS MESSED UP
//TODO: RUN PREVIEW SMOOTHLY

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
    public List<SpriteFrame> PreviewSpriteFrames = [];
    private int _previewFrameIndex;
    private long _lastPreviewStep = -1;
    private readonly Stopwatch _previewStopwatch = new();
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
    private static readonly Windows.System.VirtualKey[] NudgeTrackedKeys =
    [
        Windows.System.VirtualKey.W,
        Windows.System.VirtualKey.A,
        Windows.System.VirtualKey.S,
        Windows.System.VirtualKey.D,
        Windows.System.VirtualKey.Control,
        Windows.System.VirtualKey.LeftControl,
        Windows.System.VirtualKey.RightControl,
        Windows.System.VirtualKey.Shift,
        Windows.System.VirtualKey.LeftShift,
        Windows.System.VirtualKey.RightShift,
    ];
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
    
        ZoomNumberBox.Text = "100";

        //UpdateVisuals();

        ActualThemeChanged -= ThemeChanged;
        ActualThemeChanged += ThemeChanged;


    }

    public Border GetFramesLoadingBorder { get => FramesLoadingBorder; }

    private SubjectInterfaceConfig GetSubjectInterfaceConfig()
    {
        return (_subjectConfig.InterfaceConfig as SubjectInterfaceConfig)!;
    }

    private void CenterOriginButton_Click(object sender, RoutedEventArgs e)
    {
        GetSubjectInterfaceConfig().EditorCanvas.Pan = Vector2.Zero;
        GetSubjectInterfaceConfig().PreviewCanvas.Pan = Vector2.Zero;

        UpdateAnimationPreviewFrame();
        UpdateVisuals();
    }

    AnimationConfig getCurrentAnimationConfig()
    {
        return _subjectConfig.AnimationConfigs![_animationConfigName!];
    }

    AnimationInterfaceConfig getCurrentAnimationInterfaceConfig()
    {
        return (_subjectConfig.AnimationConfigs![_animationConfigName!].InterfaceConfig as AnimationInterfaceConfig)!;
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

    public event Action<IntVector2>? SpritePositionMoved;


    public void SetSpriteIndex(int index)
    {
        _selectedFrame = index;
        UpdateVisuals();
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
        ZoomNumberBox.ValueChanged -= ZoomNumberBox_TextChanged;

        FromNumberBox.ValueChanged -= FromNumberBox_TextChanged;
        ToNumberBox.ValueChanged -= ToNumberBox_TextChanged;
    }



    public void LoadAnimation(List<SpriteFrame> spriteFrames, SubjectConfig subjectConfig, string? animationConfigName, SKColor? backgroundSKColor)
    {
        UnsubscribeCanvases();
        foreach (var frame in PreviewSpriteFrames)
        {
            frame.WriteableBitmap?.Dispose();
        }

        PreviewSpriteFrames = spriteFrames;
  
 
        _previewFrameIndex = 0;
        _lastPreviewStep = -1;
        _previewStopwatch.Restart();
        _subjectConfig = subjectConfig;
        _animationConfigName = animationConfigName;



        CoordinateCanvas.PaintSurface += CoordinateCanvas_PaintSurface;
        AnimationPreviewCanvas.PaintSurface += AnimationPreviewCanvas_PaintSurface;

        CoordinateCanvas.SizeChanged += CoordinateCanvas_SizeChanged;
        AnimationPreviewCanvas.SizeChanged += AnimationPreviewCanvas_SizeChanged;

        if (PreviewSpriteFrames.Count > 0)
        {
            int maxFrames = Math.Max(PreviewSpriteFrames.Count - 1, 0);
            ToNumberBox.PlaceholderText = maxFrames.ToString();
            ToNumberBox.Maximum = maxFrames;
            FromNumberBox.Maximum = maxFrames;
            _previewTimer.Start();

            ShowPreviousToggleSwitch.IsOn = MainWindow.ProgramConfig.ShowPreviousFrameBehind;
            ZoomNumberBox.Text = (GetSubjectInterfaceConfig().EditorCanvas.Zoom * 100).ToString();

        

            FromNumberBox.Text = getCurrentAnimationInterfaceConfig().Range.From.ToString();
            if(getCurrentAnimationInterfaceConfig().Range.To != -1)
            {
                ToNumberBox.Text = getCurrentAnimationInterfaceConfig().Range.To.ToString();
            }
            else
            {
                ToNumberBox.Text = null;
            }

            

            if(GetSubjectInterfaceConfig().PreviewSize != null)
            {
                AnimationPreviewHostBorder.Width = GetSubjectInterfaceConfig().PreviewSize!.Value.X;
                AnimationPreviewHostBorder.Height = GetSubjectInterfaceConfig().PreviewSize!.Value.Y;
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
            ZoomNumberBox.ValueChanged += ZoomNumberBox_TextChanged;

            FromNumberBox.ValueChanged += FromNumberBox_TextChanged;
            ToNumberBox.ValueChanged += ToNumberBox_TextChanged;




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
            _previewStopwatch.Stop();
        }

        UpdateAnimationPreviewFrame();
    }

    public void UnloadAnimation()
    {


        ClearNudgeKeyState();
        LoadAnimation(new([]), _subjectConfig, _animationConfigName, null);
        UpdateVisuals();
    }

    public void UpdateVisuals()
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
            _dragStartPan = GetSubjectInterfaceConfig().EditorCanvas.Pan;
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
            GetSubjectInterfaceConfig().EditorCanvas.Pan = _dragStartPan + (currentPosition - _dragStartPointer);
            UpdateVisuals();
        }
        else if (_isFrameDragging)
        {
            var delta = currentPosition - _frameDragStartPointer;
            int dx = (int)MathF.Round(delta.X / GetSubjectInterfaceConfig().EditorCanvas.Zoom);
            int dy = (int)MathF.Round(-delta.Y / GetSubjectInterfaceConfig().EditorCanvas.Zoom);
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

        float oldZoom = GetSubjectInterfaceConfig().EditorCanvas.Zoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = CoordinateCanvas.ActualWidth / 2.0;
        double centerY = CoordinateCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + GetSubjectInterfaceConfig().EditorCanvas.Pan.X;
        double oldAxisY = centerY + GetSubjectInterfaceConfig().EditorCanvas.Pan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        GetSubjectInterfaceConfig().EditorCanvas.Zoom = newZoom;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);

        GetSubjectInterfaceConfig().EditorCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        if (_isDragging)
        {
            _dragStartPan = GetSubjectInterfaceConfig().EditorCanvas.Pan;
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
        double zoomPercent = Math.Round(GetSubjectInterfaceConfig().EditorCanvas.Zoom * 100);
      
        ZoomNumberBox.Text = zoomPercent.ToString();
        _isUpdatingZoomControls = false;
    }

    private void ZoomNumberBox_TextChanged(float? zoomValue)
    {
 
        if (_isUpdatingZoomControls)
        {
            return;
        }

        float newZoom = Math.Clamp((float)zoomValue! / 100.0f, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - GetSubjectInterfaceConfig().EditorCanvas.Zoom) < 0.0001f)
        {
            return;
        }

        float oldZoom = GetSubjectInterfaceConfig().EditorCanvas.Zoom;

        GetSubjectInterfaceConfig().EditorCanvas.Zoom = newZoom;

        double centerX = CoordinateCanvas.ActualWidth / 2.0;
        double centerY = CoordinateCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + GetSubjectInterfaceConfig().EditorCanvas.Pan.X;
        double oldAxisY = centerY + GetSubjectInterfaceConfig().EditorCanvas.Pan.Y;

        double worldX = (centerX - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - centerY) / oldZoom;

        double newAxisX = centerX - (worldX * newZoom);
        double newAxisY = centerY + (worldY * newZoom);

        GetSubjectInterfaceConfig().EditorCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        UpdateVisuals();
    }




    public void NudgeOffset(int dx, int dy)
    {
        if ((dx == 0 && dy == 0) || getCurrentAnimationConfig().FrameCongfigs == null)
        {
            return;
        }
      
        SpritePositionMoved?.Invoke(new(dx, dy));

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

    public void ClearNudgeKeyState()
    {
        _heldNudgeKeys.Clear();
        _nudgeHoldTick = 0;
        _nudgeHoldTimer.Stop();
    }

    public void SyncNudgeKeyState(Func<Windows.System.VirtualKey, bool> isKeyDown)
    {
        bool hadDirection = HasHeldDirectionKey();
        _heldNudgeKeys.Clear();
        foreach (var key in NudgeTrackedKeys)
        {
            if (isKeyDown(key))
            {
                _heldNudgeKeys.Add(key);
            }
        }

        UpdateNudgeMotionState(hadDirection);
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
        _previewDragStartPan = GetSubjectInterfaceConfig().PreviewCanvas.Pan;
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
        GetSubjectInterfaceConfig().PreviewCanvas.Pan = _previewDragStartPan + (currentPosition - _previewDragStartPointer);
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

        GetSubjectInterfaceConfig().PreviewSize = new((float)width, (float)height);
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

        float oldZoom = GetSubjectInterfaceConfig().PreviewCanvas.Zoom;
        float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
        float newZoom = Math.Clamp(oldZoom * zoomMultiplier, MinPreviewZoom, MaxPreviewZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
        {
            return;
        }

        double centerX = AnimationPreviewCanvas.ActualWidth / 2.0;
        double centerY = AnimationPreviewCanvas.ActualHeight / 2.0;
        double oldAxisX = centerX + GetSubjectInterfaceConfig().PreviewCanvas.Pan.X;
        double oldAxisY = centerY + GetSubjectInterfaceConfig().PreviewCanvas.Pan.Y;

        double worldX = (point.Position.X - oldAxisX) / oldZoom;
        double worldY = (oldAxisY - point.Position.Y) / oldZoom;

        GetSubjectInterfaceConfig().PreviewCanvas.Zoom = newZoom;

        double newAxisX = point.Position.X - (worldX * newZoom);
        double newAxisY = point.Position.Y + (worldY * newZoom);
        GetSubjectInterfaceConfig().PreviewCanvas.Pan = new Vector2((float)(newAxisX - centerX), (float)(newAxisY - centerY));
        if (_isPreviewDragging)
        {
            _previewDragStartPan = GetSubjectInterfaceConfig().PreviewCanvas.Pan;
            _previewDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
        }

        UpdateAnimationPreviewFrame();
        e.Handled = true;
    }

    int GetCorrectRangeToValue()
    {
        if(getCurrentAnimationInterfaceConfig().Range.To == -1)
        {
            return Math.Max(PreviewSpriteFrames.Count - 1, 0);
        }

        return getCurrentAnimationInterfaceConfig().Range.To;
    }

    private void PreviewTimer_Tick(object? sender, object e)
    {
        if (PreviewSpriteFrames.Count == 0)
        {
            _previewTimer.Stop();
            _previewStopwatch.Stop();
            return;
        }

        int frameCount = GetPreviewFrameCount();
        if (frameCount <= 0)
        {
            return;
        }

        int delayInTicks = Math.Max(1, getCurrentAnimationConfig().Delay);
        long previewStep = (long)(_previewStopwatch.Elapsed.TotalSeconds * 60.0 / delayInTicks);
        if (previewStep == _lastPreviewStep)
        {
            return;
        }

        _lastPreviewStep = previewStep;
        int nextFrameIndex = (int)(previewStep % frameCount);
        if (nextFrameIndex == _previewFrameIndex)
        {
            return;
        }

        _previewFrameIndex = nextFrameIndex;
        UpdateAnimationPreviewFrame();
    }

    private int GetPreviewFrameCount()
    {
        return GetCorrectRangeToValue() + 1 - getCurrentAnimationInterfaceConfig().Range.From;
    }

    private void UpdateAnimationPreviewFrame()
    {
        AnimationPreviewCanvas.Invalidate();
    }

 




    private void CoordinateCanvas_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        int width = e.Info.Width;
        int height = e.Info.Height;

        canvas.Clear();

        float zoom = GetSubjectInterfaceConfig().EditorCanvas.Zoom;
        var pan = GetSubjectInterfaceConfig().EditorCanvas.Pan;
        float axisX = width / 2f + pan.X;
        float axisY = height / 2f + pan.Y;

        DrawCheckerboard(canvas, width, height, axisX, axisY, zoom);

        if (PreviewSpriteFrames.Count == 0)
        {
            return;
        }

        ColorHelper.TryParse(_subjectConfig.Processing.BackgroundColor, out byte a, out byte r, out byte g, out byte b);

        if (MainWindow.ProgramConfig.ShowPreviousFrameBehind)
        {
            int previousFrameIndex = _selectedFrame == 0 ? PreviewSpriteFrames.Count - 1 : _selectedFrame - 1;
            SpriteFrame? previousSpriteFrame = PreviewSpriteFrames[previousFrameIndex];
    
            if (previousSpriteFrame != null)
            {
                var destRect = DrawFrame(canvas, previousFrameIndex, getCurrentAnimationConfig().FrameCongfigs[previousFrameIndex].Offset, zoom, axisX, axisY, width, height, 0.5f, _previousFramePaint);

                canvas.DrawRect(destRect, _secondaryBorderPaint);




                float l = destRect.Left + previousSpriteFrame.CroppedRect.Left * zoom;
                float t = destRect.Top + previousSpriteFrame.CroppedRect.Top * zoom;
                float r2 = destRect.Left + previousSpriteFrame.CroppedRect.Right * zoom;
                float b2 = destRect.Top + previousSpriteFrame.CroppedRect.Bottom * zoom;

                SKRect transformedRect = new(l, t, r2, b2);

                canvas.DrawRect(transformedRect, _secondaryBorderPaint);

                canvas.DrawLine(destRect.Left, destRect.Top, transformedRect.Left, transformedRect.Top, _secondaryBorderPaint);
                canvas.DrawLine(destRect.Right, destRect.Top, transformedRect.Right, transformedRect.Top, _secondaryBorderPaint);
                canvas.DrawLine(destRect.Left, destRect.Bottom, transformedRect.Left, transformedRect.Bottom, _secondaryBorderPaint);
                canvas.DrawLine(destRect.Right, destRect.Bottom, transformedRect.Right, transformedRect.Bottom, _secondaryBorderPaint);
            }
        }

        SpriteFrame? currentSpriteFrame = PreviewSpriteFrames[_selectedFrame];
        if (currentSpriteFrame != null)
        {
            var destRect = DrawFrame(canvas, _selectedFrame, getCurrentFrameConfig().Offset, zoom, axisX, axisY, width, height, MainWindow.ProgramConfig.ShowPreviousFrameBehind ? 0.5f : 1f);
            
            canvas.DrawRect(destRect, _borderPaint);            
           



            float l = destRect.Left + currentSpriteFrame.CroppedRect.Left * zoom;
            float t = destRect.Top + currentSpriteFrame.CroppedRect.Top * zoom;
            float r2 = destRect.Left + currentSpriteFrame.CroppedRect.Right * zoom;
            float b2 = destRect.Top + currentSpriteFrame.CroppedRect.Bottom * zoom;

            SKRect transformedRect = new SKRect(l, t, r2, b2);

            canvas.DrawRect(transformedRect, _borderPaint);

            canvas.DrawLine(destRect.Left, destRect.Top, transformedRect.Left, transformedRect.Top, _borderPaint);
            canvas.DrawLine(destRect.Right, destRect.Top, transformedRect.Right, transformedRect.Top, _borderPaint);
            canvas.DrawLine(destRect.Left, destRect.Bottom, transformedRect.Left, transformedRect.Bottom, _borderPaint);
            canvas.DrawLine(destRect.Right, destRect.Bottom, transformedRect.Right, transformedRect.Bottom, _borderPaint);
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

        float zoom = GetSubjectInterfaceConfig().PreviewCanvas.Zoom;
        var pan = GetSubjectInterfaceConfig().PreviewCanvas.Pan;
        float axisX = width / 2f + pan.X;
        float axisY = height / 2f + pan.Y;

        DrawCheckerboard(canvas, width, height, axisX, axisY, zoom);

        if (PreviewSpriteFrames.Count == 0)
        {
            return;
        }

        int previewFrameIndex = Math.Clamp(_previewFrameIndex + getCurrentAnimationInterfaceConfig().Range.From, getCurrentAnimationInterfaceConfig().Range.From, GetCorrectRangeToValue());
     
     
        
            IntVector2 previewOffset = previewFrameIndex < getCurrentAnimationConfig().FrameCongfigs.Count
                ? getCurrentAnimationConfig().FrameCongfigs[previewFrameIndex].Offset
                : new IntVector2();
            DrawFrame(canvas, previewFrameIndex, previewOffset, zoom, axisX, axisY, width, height, 1f);
        




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

    private SKRect DrawFrame(SKCanvas canvas, int spriteFrameIndex, IntVector2 offset, float zoom, float axisX, float axisY, int viewportWidth, int viewportHeight, float alpha, SKPaint? overridePaint = null)
    {

        float width = Math.Max(1f, PreviewSpriteFrames[spriteFrameIndex].OriginalSize.X * zoom);
        float height = Math.Max(1f, PreviewSpriteFrames[spriteFrameIndex].OriginalSize.Y * zoom);
        float x = axisX + (offset.X * zoom);
        float y = axisY - (offset.Y * zoom);
        SKRect destRect = new(x, y, x + width, y + height);

        SKRect croppedDestRect;
        if (PreviewSpriteFrames[spriteFrameIndex].OriginalSize.Y != PreviewSpriteFrames[spriteFrameIndex].WriteableBitmap.Info.Size.Height || PreviewSpriteFrames[spriteFrameIndex].OriginalSize.X != PreviewSpriteFrames[spriteFrameIndex].WriteableBitmap.Info.Size.Width)
        {
            float cx = axisX + ((offset.X + PreviewSpriteFrames[spriteFrameIndex].CroppedRect.Left) * zoom);
            float cy = axisY - ((offset.Y - PreviewSpriteFrames[spriteFrameIndex].CroppedRect.Top) * zoom);
            croppedDestRect = new(cx, cy, cx + width, cy + height);
        }
        else
        {
            croppedDestRect = destRect;
        }
       
        SKRect viewportRect = new(0, 0, viewportWidth, viewportHeight);
        if (!croppedDestRect.IntersectsWith(viewportRect))
        {
            return croppedDestRect;
        }

 
        SKRect clippedDest = SKRect.Intersect(croppedDestRect, viewportRect);
        float invScaleX = PreviewSpriteFrames[spriteFrameIndex].OriginalSize.X / width;
        float invScaleY = PreviewSpriteFrames[spriteFrameIndex].OriginalSize.Y / height;
        SKRect sourceRect = new(
            (clippedDest.Left - croppedDestRect.Left) * invScaleX,
            (clippedDest.Top - croppedDestRect.Top) * invScaleY,
            (clippedDest.Right - croppedDestRect.Left) * invScaleX,
            (clippedDest.Bottom - croppedDestRect.Top ) * invScaleY);

        SKPaint paint = overridePaint ?? _spritePaint;

        paint.Color = new SKColor(255, 255, 255, (byte)(alpha * 255f));
 
        canvas.DrawBitmap(PreviewSpriteFrames[spriteFrameIndex].WriteableBitmap, sourceRect, clippedDest, paint);

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

 

    private void FromNumberBox_TextChanged(float? fromValue)
    {
     
        getCurrentAnimationInterfaceConfig().Range.From = (int)fromValue!;
        ToNumberBox.Minimum = getCurrentAnimationInterfaceConfig().Range.From;
    }

    private void ToNumberBox_TextChanged(float? toValue)
    {
  
        var max = Math.Max(PreviewSpriteFrames.Count - 1, 0);
        if (toValue == null || toValue == max)
        {
            getCurrentAnimationInterfaceConfig().Range.To = -1;
            FromNumberBox.Maximum = max;
            return;
        }

        getCurrentAnimationInterfaceConfig().Range.To = (int)toValue;
        FromNumberBox.Maximum = (double)toValue;
    }

    private void ApplyHeldNudgeKeys()
    {
        if (MainWindow.IsCtrlHeld) return;

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
