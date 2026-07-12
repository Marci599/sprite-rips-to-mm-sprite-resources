using CommunityToolkit.WinUI;
using FramesToMMSpriteResources.DataConfig;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

//TODO: WHEN THERE IS A NODE SAVED AS SELECTED, BUT THE FOLDER/SPRITE GETS REMOVED, PROGRAM CRASHES
//TODO: REMOVE UNUSED OFFSETS AFTER GENERATION
//TODO: HANDLE NUMBER PARSING
namespace FramesToMMSpriteResources
{
    public enum ItemDepth
    {
        Subject = 0,
        Animation = 1,
        Frame = 2
    }

    public partial class TreeItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text { get; set; }

        public ItemDepth Depth { get; set; }

        public string CountText { get; set; }

        public int Count;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public TreeItem(string text, ItemDepth depth, int count = -1, bool isSelected = false)
        {
            Text = text;
            Depth = depth;
            Count = count;
            CountText = count.ToString();
            _isSelected = isSelected;
        }

        public TreeItem(string text, ItemDepth depth, int oldCount, int newCount, bool isSelected = false)
        {
            Text = text;
            Depth = depth;
            Count = newCount;
            CountText = /*oldCount + " → " + */newCount.ToString();
            _isSelected = isSelected;
        }
    }

    public struct IntVector2(int x, int y)
    {
        public int X { get; set; } = x;
        public int Y { get; set; } = y;

        public readonly bool Equals(IntVector2 other)
            => X == other.X && Y == other.Y;

        public override readonly bool Equals(object? obj)
            => obj is IntVector2 other && Equals(other);

        public override readonly int GetHashCode()
            => HashCode.Combine(X, Y);

        public static bool operator ==(IntVector2 left, IntVector2 right)
            => left.Equals(right);

        public static bool operator !=(IntVector2 left, IntVector2 right)
            => !left.Equals(right);

        public override readonly string ToString()
            => $"({X}, {Y})";
    }

    public class SpriteFrame
    {
        public SKBitmap WriteableBitmap;
        public SKRectI CroppedRect;
        public IntVector2 OriginalSize;

        public SpriteFrame(SKBitmap writeableBitmap, SKRectI croppedRect, IntVector2 originalSize)
        {
            WriteableBitmap = writeableBitmap;
            CroppedRect = croppedRect;
            OriginalSize = originalSize;
        }
    }

    public sealed partial class MainWindow : Window
    {
        private static readonly string CONFIG_FILENAME = "config.json";
        private static readonly string INTERFACE_CONFIG_FILENAME = "interface.json";

        public static string WorkingPath = AppContext.BaseDirectory;

        public static ProgramConfig ProgramConfig;

        private HashSet<object> _currentConfigs;

        bool _isPanelChangeInProgress = false;

        public bool IsPanelChangeInProgress
        {
            get => _isPanelChangeInProgress;
            set
            {
                _isPanelChangeInProgress = value;
                CheckForAllowNavigating();
            }
        }

        void CheckForAllowNavigating()
        {
            TreeViewControl.IsEnabled = HeaderBreadcrumbBar.IsEnabled = SettingsToggleButton.IsEnabled = (!_isPanelChangeInProgress && _isWindowActive && !_isGenerating);
        }


        bool _isActivated = false;
 

        bool _isHierarchyError = true;

        private const int _fadeOutMs = 50;
        private const int _fadeInMs = 100;

        private static bool _isCtrlHeld = false;
        public static bool IsCtrlHeld => _isCtrlHeld;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private bool _isGeneratePanelShowed = true;

        public string[] AnimationSpriteFramePath = new string[3];

        public ObservableCollection<string> BreadcrumbItems { get; } = new();

        public ObservableCollection<string> WorkingPathHistory { get; } = new();

        public ObservableCollection<AlsoKnownAsEntry> AlsoKnownAsEntries { get; } = new();

        private readonly JsonSerializerOptions jsonOptions = new()
        { 
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            IncludeFields = true,
            TypeInfoResolverChain =
            {
                ConfigJsonContext.Default
            }
        };

        bool _isWindowActive = false;
        public bool IsWindowActive
        {
            get => _isWindowActive;
            set
            {
                _isWindowActive = value;
                CheckForAllowFrameEditing();
            }
        }

        bool _isGenerating = false;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                _isGenerating = value;
                CheckForAllowProgramEditing();
            }
        }

        bool _isEnoughFrames = false;
        public bool IsEnoughFrames
        {
            get => _isEnoughFrames;
            set
            {
                _isEnoughFrames = value;
                CheckForAllowGenerating();
            }
        }

        bool _isLoadingFrames = false;
        public bool IsLoadingFrames
        {
            get => _isLoadingFrames;
            set
            {
                _isLoadingFrames = value;
                CheckForAllowFrameEditing();
            }
        }

        void CheckForAllowFrameEditing()
        {
            if (!_isLoadingFrames)
            {
                FrameCoordinateEditorControl.GetFramesLoadingBorder.Child.Visibility = Visibility.Collapsed;
            }
            else
            {
                FrameCoordinateEditorControl.GetFramesLoadingBorder.Child.Visibility = Visibility.Visible;
            }

            if (!_isGenerating && !_isLoadingFrames && _isWindowActive)
            {
                FrameCoordinateEditorControl.GetFramesLoadingBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                FrameCoordinateEditorControl.GetFramesLoadingBorder.Visibility = Visibility.Visible;
            }
            
        }

        void CheckForAllowGenerating()
        {
            bool isEnabled = (!_isGenerating && _isEnoughFrames);

            ReduceFileSizeCheckBox.IsEnabled = isEnabled;
            GenerateButton.IsEnabled = isEnabled;
            if (isEnabled)
            {
                ReduceFileSizeCheckBoxTexts.Opacity = 1;
            }
            else
            {
                ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
            }
        }

        void CheckForAllowProgramEditing()
        {
            ControlEnabler.IsEnabled = !_isGenerating;
            CheckForAllowNavigating();
            CheckForAllowGenerating();
            CheckForAllowFrameEditing();
        }


        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TreeItem))]
        public MainWindow()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 625));
            AppWindow.SetIcon("Assets/icon.ico");

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 735;
            presenter.PreferredMinimumHeight = 400;

            AppWindow.SetPresenter(presenter);

            ProgramConfig = LoadProgramConfig();

            SetUpTreeViewAndConfigs();

            Activated += MainWindow_Activated;

            HeaderBreadcrumbBar.ItemsSource = BreadcrumbItems;
            HeaderBreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;

            ProcessingCardControl.GetCropSpritesCheckBox.Click -= CropSpritesCheckBox_Click;
            ProcessingCardControl.GetCropSpritesCheckBox.Click += CropSpritesCheckBox_Click;

            ProcessingOverwriteCardControl.GetCropSpritesCheckBox.Click -= CropSpritesOverwriteCheckBox_Click;
            ProcessingOverwriteCardControl.GetCropSpritesCheckBox.Click += CropSpritesOverwriteCheckBox_Click;

            ProgramNameTextBlock.Text += GetCurrentVersion(); 
        
            AppWindow.Closing += AppWindow_Closing;

            if (Content is UIElement root)
            {
                root.AddHandler(UIElement.PointerPressedEvent,
                    new PointerEventHandler(MainWindow_PointerPressed),
                    handledEventsToo: true);
            }

            CheckForUpdateIfNeeded();
        }

        bool _ableToRelaod = true;
        bool _waitingForSecondaryActivation = false;

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (!_ableToRelaod)
                {
                    _waitingForSecondaryActivation = true;
                    return;
                }

                ActivateProgram();
            }
            else
            {           
                _ableToRelaod = false;
                
                if (!_waitingForSecondaryActivation)
                {
                    _waitingForSecondaryActivation = false;          
                    IsWindowActive = false;

                    ClearKeyboardState();
                                
                    FrameCoordinateEditorControl.UnloadAnimation();

                    cts?.Cancel();

                    SaveAllConfigs();
                }
                else
                {
                    _waitingForSecondaryActivation = false;
                }
                         
                _ableToRelaod = true;
            }
        }

        private void MainWindow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_waitingForSecondaryActivation)
            {
                ActivateProgram();
                _ableToRelaod = true;
            }
        }

        async void ActivateProgram()
        {
            _waitingForSecondaryActivation = false;
            
            if (_isActivated)
            {
                ProgramConfig = LoadProgramConfig();
                ReloadTreeViewAndConfigs();
            }
            _isActivated = true;

            SyncWorkingPathHistoryFromConfig();

            ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
            ReduceFileSizeCheckBox.IsChecked = ProgramConfig.ReduceFileSize;
            ReduceFileSizeCheckBox.Click += ReduceFileSizeCheckBox_Click;

            WorkingPathTextBox.TextChanged -= WorkingPathTextBox_TextChanged;
            WorkingPathTextBox.Text = ProgramConfig.WorkingPath;
            WorkingPathTextBox.TextChanged += WorkingPathTextBox_TextChanged;


            IsWindowActive = true;
            IsPanelChangeInProgress = false;

            SyncKeyboardState();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                SetInfoBar(InfoBarSeverity.Informational, "Saving", "The program will close soon");
                SaveAllConfigs();
                Close();
            });
        }

        private async void WorkingPathTextBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            SaveAllConfigs();
            ProgramConfig.WorkingPath = sender.Text;
            AddWorkingPathToHistoryIfValid(sender.Text);
            SyncWorkingPathHistoryFromConfig();
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && WorkingPathHistory.Count > 0)
            {
                sender.IsSuggestionListOpen = true;
            }
            FrameCoordinateEditorControl.UnloadAnimation();
            ReloadTreeViewAndConfigs();
        }

        private void WorkingPathTextBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string path)
            {
                sender.Text = path;
            }
        }

        private void WorkingPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            SyncWorkingPathHistoryFromConfig();
            if (WorkingPathHistory.Count > 0)
            {
                WorkingPathTextBox.IsSuggestionListOpen = true;
            }
        }

        private void SyncWorkingPathHistoryFromConfig()
        {
            WorkingPathHistory.Clear();
            foreach (var path in GetValidWorkingPathHistory(WorkingPathTextBox.Text))
            {
                WorkingPathHistory.Add(path);
            }
        }

        private IEnumerable<string> GetValidWorkingPathHistory(string? excludedPath = null)
        {
            return (ProgramConfig.WorkingPathHistory ?? [])
                .Where(path => !string.Equals(path, excludedPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10);
        }

        private bool IsValidWorkingPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            try
            {
                return !File.Exists(Path.Combine(path, "FramesToMMSpriteResources.dll")) && AreSubjectsCorrect(path);
            }
            catch
            {
                return false;
            }
        }

        private void AddWorkingPathToHistoryIfValid(string? path)
        {
            if (!IsValidWorkingPath(path))
            {
                return;
            }

            ProgramConfig.WorkingPathHistory ??= [];
            ProgramConfig.WorkingPathHistory.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
            ProgramConfig.WorkingPathHistory.Insert(0, path!);
            ProgramConfig.WorkingPathHistory = GetValidWorkingPathHistory()
                .ToList();

            SyncWorkingPathHistoryFromConfig();
        }

        private void GeneratePathTextBox_TextChanged(object sender, RoutedEventArgs e)
        {
            ProgramConfig.AssetConfig!.InterfaceConfig.GeneratePath = (sender as TextBox)!.Text;
        }

        private void ReduceFileSizeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            ProgramConfig.ReduceFileSize = (sender as CheckBox)!.IsChecked!.Value;
        }

        void SaveAllConfigs()
        { 
            SaveProgramConfig();
            if (!_isHierarchyError)
            {
                SaveJson(Path.Combine(WorkingPath, CONFIG_FILENAME), ProgramConfig.AssetConfig!);
                SaveJson(Path.Combine(WorkingPath, INTERFACE_CONFIG_FILENAME), ProgramConfig.AssetConfig!.InterfaceConfig);
                var subjectDirs = Directory.GetDirectories(WorkingPath);
                foreach (var subjectDir in subjectDirs)
                {
                    string subjectName = Path.GetFileName(subjectDir);
                    if (subjectName != "_generated")
                    {
                        SaveJson(Path.Combine(subjectDir, CONFIG_FILENAME), ProgramConfig.AssetConfig!.SubjectConfigs![subjectName]);
                        SaveJson(Path.Combine(subjectDir, INTERFACE_CONFIG_FILENAME), ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].InterfaceConfig);

                        var animationDirs = Directory.GetDirectories(subjectDir);
                        foreach (var animationDir in animationDirs)
                        {
                            string animationName = Path.GetFileName(animationDir);

                            SaveJson(Path.Combine(animationDir, CONFIG_FILENAME), ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName]);
                            SaveJson(Path.Combine(animationDir, INTERFACE_CONFIG_FILENAME), ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName].InterfaceConfig);
                        }
                    }

                }
            }             
        }

        private void SaveJson(string filePath, object classToSave)
        {
            try
            {
                var json = JsonSerializer.Serialize(classToSave, jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                var title = "Config save Failed";
                var filename = string.IsNullOrEmpty(filePath) ? "" : Path.GetFileName(filePath);
                SetInfoBar(InfoBarSeverity.Error, title, $"Could not save {filename}\n{ex.Message}");
            }
        }

        private T LoadJson<T>(string filePath) where T : new()
        {
            try
            {
                if (!File.Exists(filePath))
                    return new T();

                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json, jsonOptions);

                return obj ?? new T();
            }
            catch (Exception ex)
            {
                var title = "Config failed to load";
                SetInfoBar(InfoBarSeverity.Error, title, $"Could not load {filePath}\n{ex.Message}");

                return new T();
            }
        }

        private static string GetUserConfigDirectory()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FramesToSpriteResources");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        private ProgramConfig LoadProgramConfig()
        {
            var configPath = Path.Combine(GetUserConfigDirectory(), CONFIG_FILENAME);

            //Debug.WriteLine(configPath);
            //Debug.WriteLine(Path.Exists(configPath));
            var loaded = LoadJson<ProgramConfig>(configPath);
            return loaded;
        }

        void SaveProgramConfig()
        {
            var configPath = Path.Combine(GetUserConfigDirectory(), CONFIG_FILENAME);
            SaveJson(configPath, ProgramConfig);
            //Debug.WriteLine(configPath);
        }

        void ReloadTreeViewAndConfigs()
        {
            TreeViewControl.RootNodes.Clear();
            ProgramConfig.AssetConfig = null;
            TryCloseInfoBar();
            SetUpTreeViewAndConfigs();
        }

        void SetUpTreeViewAndConfigs()
        {
            _isHierarchyError = false;
                              
            TreeViewControl.ItemInvoked -= TreeViewControl_ItemInvoked;
            TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;

            TreeViewControl.PointerPressed -= TreeViewControl_PointerPressed;
            TreeViewControl.PointerPressed += TreeViewControl_PointerPressed;

            TreeViewControl.Expanding -= TreeViewControl_Expanding;
            TreeViewControl.Expanding += TreeViewControl_Expanding;

            TreeViewControl.Collapsed -= TreeViewControl_Collapsed;
            TreeViewControl.Collapsed += TreeViewControl_Collapsed;

            AssetConfigBorder.Visibility = Visibility.Collapsed;
            WorkingPathTextBox.CornerRadius = new CornerRadius(4, 0, 0, 4);
            BrowseFolderButton.CornerRadius = new CornerRadius(0, 4, 4, 0);
            GeneratePathTextBox.TextChanged -= GeneratePathTextBox_TextChanged;
            IsHdCheckBox.Click -= ClickIsHdCheckBox;

            WorkingPath = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(ProgramConfig.WorkingPath))
            {
                WorkingPath = ProgramConfig.WorkingPath;
            }

            AddWorkingPathToHistoryIfValid(ProgramConfig.WorkingPath);

            if (!Directory.Exists(WorkingPath))
            {
                _isHierarchyError = true;
                SetInfoBar(InfoBarSeverity.Error, "Working direcotry path is incorrect", $"{WorkingPath} does not exist", false);
                TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                OpenSettingsAndHideGeneratePanelImmediately();
                return;
            }

            if (Directory.GetDirectories(WorkingPath).Length == 0)
            {
                _isHierarchyError = true;
                TreeViewPlaceHolderText.Text = "Empty working directory";
                TreeViewPlaceHolderButton.Visibility = Visibility.Visible;
                TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                OpenSettingsAndHideGeneratePanelImmediately();
                return;
            }
  
            var firstLevelFiles = Directory.GetFiles(WorkingPath);
          
            if (!firstLevelFiles.Contains(Path.Combine(WorkingPath, "FramesToMMSpriteResources.dll")) && AreSubjectsCorrect())
            {            
                SetUpSubjectTreeViewAndConfigs();
                TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;

                if (TreeViewControl.SelectedNode == null)
                {
                    OpenSettingsAndHideGeneratePanelImmediately();
                }
                else
                {
                    ChangeConfigPanelIfNecessary(TreeViewControl.SelectedNode, false, true);
                }
            }
            else
            {
                _isHierarchyError = true;
    
                TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                OpenSettingsAndHideGeneratePanelImmediately();
                if (!string.IsNullOrWhiteSpace(ProgramConfig.WorkingPath))
                {
                    SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong...", false);
                }
            }            
        }

        void SetUpSubjectTreeViewAndConfigs()
        {
            AssetConfigBorder.Visibility = Visibility.Visible;
            WorkingPathTextBox.CornerRadius = new CornerRadius(4, 0, 0, 0);
            BrowseFolderButton.CornerRadius = new CornerRadius(0, 4, 0, 0);
            ProgramConfig.AssetConfig = LoadJson<AssetConfig>(Path.Combine(WorkingPath, CONFIG_FILENAME));
            ProgramConfig.AssetConfig.InterfaceConfig = LoadJson<AssetInterfaceConfig>(Path.Combine(WorkingPath, INTERFACE_CONFIG_FILENAME));

            ProgramConfig.SelectedNodePath ??= [];

            GeneratePathTextBox.Text = ProgramConfig.AssetConfig!.InterfaceConfig.GeneratePath;
            IsHdCheckBox.IsChecked = ProgramConfig.AssetConfig!.IsHd;
            GeneratePathTextBox.TextChanged += GeneratePathTextBox_TextChanged;
            IsHdCheckBox.Click += ClickIsHdCheckBox;

            var subjectDirs = Directory.GetDirectories(WorkingPath);

            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);
                if (subjectName != "_generated")
                {        
                    SubjectConfig subjectConfig = LoadJson<SubjectConfig>(Path.Combine(subjectDir, CONFIG_FILENAME));
                    subjectConfig.InterfaceConfig = LoadJson<SubjectInterfaceConfig>(Path.Combine(subjectDir, INTERFACE_CONFIG_FILENAME));
                    subjectConfig.Processing.BackgroundColor = subjectConfig.BackgroundColor;
                    subjectConfig.Processing.ColorTreshold = subjectConfig.ColorTreshold;
                    subjectConfig.Processing.RemoveBackground = subjectConfig.RemoveBackground;
                    subjectConfig.Processing.ResizeToPercent = subjectConfig.ResizeToPercent;
                    subjectConfig.Processing.FilterMode = subjectConfig.FilterMode;
                    subjectConfig.Processing.MipmapMode = subjectConfig.MipmapMode;
                    subjectConfig.Processing.CropLeft = subjectConfig.CropLeft;
                    subjectConfig.Processing.CropTop = subjectConfig.CropTop;
                    subjectConfig.Processing.CropRight = subjectConfig.CropTop;
                    subjectConfig.Processing.CropBottom = subjectConfig.CropBottom;

                    var subjectTreeItem = new TreeViewNode { Content = new TreeItem(subjectName, ItemDepth.Subject), IsExpanded = subjectConfig.InterfaceConfig.IsExpanded };

                    var animationDirs = Directory.GetDirectories(subjectDir);
                    int framesSum = 0;
                    foreach (var animationDir in animationDirs)
                    {
                        string animationName = Path.GetFileName(animationDir);
     
                        AnimationConfig animationConfig = LoadJson<AnimationConfig>(Path.Combine(animationDir, CONFIG_FILENAME));
                        animationConfig.InterfaceConfig = LoadJson<AnimationInterfaceConfig>(Path.Combine(animationDir, INTERFACE_CONFIG_FILENAME));                           
                        
                        TreeItem treeItem;
                        treeItem = new(animationName, ItemDepth.Animation);                      

                        var animationTreeItem = new TreeViewNode { Content = treeItem, IsExpanded = animationConfig.InterfaceConfig.IsExpanded };

                        int frameIndex = 0;

                        animationConfig.FrameCongfigs ??= [];

                        var frameFiles = Directory.EnumerateFiles(animationDir, "*.png");
                        foreach (var frameFile in frameFiles)
                        {                  
                            string fileName = Path.GetFileNameWithoutExtension(frameFile);
                            if (frameIndex < animationConfig.FrameCongfigs.Count)
                            {
                                animationConfig.FrameCongfigs[frameIndex].Name = fileName;
                            }
                            else
                            {
                                animationConfig.FrameCongfigs.Add(new FrameConfig(fileName));
                            }

                            string frameName = frameIndex.ToString("D4");
                            TreeItem frameTreeItem = new(frameName, ItemDepth.Frame);
                            var frameTreeViewNode = new TreeViewNode { Content = frameTreeItem };

                            animationTreeItem.Children.Add(frameTreeViewNode);

                            if (ProgramConfig.SelectedNodes != null &&
                                ProgramConfig.SelectedNodePath!.Count == 2 &&
                                    
                                ProgramConfig.SelectedNodePath[0] == subjectName &&
                                ProgramConfig.SelectedNodePath[1] == animationName &&
                                ProgramConfig.SelectedNodes.Contains(frameName))
                            {
                                (frameTreeViewNode.Content as TreeItem)!.IsSelected = true;

                                if (ProgramConfig.SelectedNodes.Last() == frameName)
                                {
                                    TreeViewControl.SelectedNode = frameTreeViewNode;
                                }
                            }
                            frameIndex++;                           
                        }

                        if (animationConfig.GetInterfaceConfig().GeneratedFrameCount == -1)
                        {
                            animationConfig.GetInterfaceConfig().GeneratedFrameCount = frameIndex;
                        }

                        if (animationConfig.GetInterfaceConfig().GeneratedFrameCount == frameIndex)
                        {
                            (animationTreeItem.Content as TreeItem)!.Count = frameIndex;
                            (animationTreeItem.Content as TreeItem)!.CountText = frameIndex.ToString();
          
                        }
                        else
                        {
                            (animationTreeItem.Content as TreeItem)!.Count = frameIndex;
                            (animationTreeItem.Content as TreeItem)!.CountText = /*$"{animationConfig.GeneratedFrameCount} → */frameIndex.ToString();
                        }

                        framesSum += frameIndex;
                        subjectConfig.AnimationConfigs![animationName] = animationConfig;
                        subjectTreeItem.Children.Add(animationTreeItem);

                        if (ProgramConfig.SelectedNodes != null &&
                            ProgramConfig.SelectedNodePath!.Count == 1 &&
                            ProgramConfig.SelectedNodePath[0] == subjectName &&
                            ProgramConfig.SelectedNodes.Contains(animationName))
                        {
                            (animationTreeItem.Content as TreeItem)!.IsSelected = true;

                            if (ProgramConfig.SelectedNodes.Last() == animationName)
                            {
                                TreeViewControl.SelectedNode = animationTreeItem;
                            }
                        }
                    }

                    (subjectTreeItem.Content as TreeItem)!.Count = framesSum;
                    (subjectTreeItem.Content as TreeItem)!.CountText = framesSum.ToString();

                    ProgramConfig.AssetConfig!.SubjectConfigs![subjectName] = subjectConfig;

                    TreeViewControl.RootNodes.Add(subjectTreeItem);
                    if (ProgramConfig.SelectedNodePath != null &&
                    ProgramConfig.SelectedNodes != null &&
                    ProgramConfig.SelectedNodePath.Count == 0 &&
                    ProgramConfig.SelectedNodes.Contains(subjectName))
                    {
                        (subjectTreeItem.Content as TreeItem)!.IsSelected = true;

                        if (ProgramConfig.SelectedNodes.Last() == subjectName)
                        {
                            TreeViewControl.SelectedNode = subjectTreeItem;
                        }
                    }
                }       
            }
        }

        void UpdateBreadcrumb(params string[] items)
        {
            BreadcrumbItems.Clear();
            foreach (var item in items)
                BreadcrumbItems.Add(item);
        }

        private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            int clickedIndex = args.Index;

            while (ProgramConfig.SelectedNodePath!.Count > clickedIndex + 1)
            {
                ProgramConfig.SelectedNodePath.RemoveAt(ProgramConfig.SelectedNodePath.Count - 1);
            }

            TreeViewNode? selectedNode = null;

            foreach (TreeViewNode subjectNode in TreeViewControl.RootNodes)
            {
                if ((subjectNode.Content as TreeItem)!.Text == ProgramConfig.SelectedNodePath[0])
                {
                    if (ProgramConfig.SelectedNodePath.Count == 1)
                    {
                        TreeViewControl.SelectedNode = subjectNode;
                        selectedNode = subjectNode;
                        break;
                    }
                    else
                    {
                        foreach (TreeViewNode animationNode in subjectNode.Children)
                        {
                            if ((animationNode.Content as TreeItem)!.Text == ProgramConfig.SelectedNodePath[1])
                            {
                                if (ProgramConfig.SelectedNodePath.Count == 2)
                                {
                                    TreeViewControl.SelectedNode = animationNode;
                                    selectedNode = animationNode;
                                    break;
                                }
                            }
                        }
                        break;
                    }
                }
            }

            FadeOutAllPanels(false, true);
            ChangeConfigPanelAsync(selectedNode!, true);
        }

        private async void AnimateGeneratePanel(bool show)
        {
            if (_isGeneratePanelShowed == show) return;
            _isGeneratePanelShowed = show;

            var compositor = ElementCompositionPreview.GetElementVisual(SaveBarBorder).Compositor;
            var saveBarVisual = ElementCompositionPreview.GetElementVisual(SaveBarBorder);
            var bottomPanelVisual = ElementCompositionPreview.GetElementVisual(BottomBarStackPanel);

            var animationDuration = TimeSpan.FromMilliseconds(_fadeOutMs + _fadeInMs);

            if (show)
            {
                PrimaryInfoBar.CornerRadius = new CornerRadius(8, 8, 0, 0);

                var cubicEaseOut = compositor.CreateCubicBezierEasingFunction(
                       new System.Numerics.Vector2(0.215f, 0.61f),
                       new System.Numerics.Vector2(0.355f, 1.0f)
                   );

                var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();

                opacityAnimation.InsertKeyFrame(0f, 0f);
                opacityAnimation.InsertKeyFrame(1f, 1f, cubicEaseOut);

                opacityAnimation.Duration = animationDuration;

                saveBarVisual.StartAnimation("Opacity", opacityAnimation);

                var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();

                offsetAnimation.InsertKeyFrame(0f, new Vector3(0, (float)SaveBarBorder.ActualHeight, 0));
                offsetAnimation.InsertKeyFrame(1f, Vector3.Zero, cubicEaseOut);

                offsetAnimation.Duration = animationDuration;

                bottomPanelVisual.StartAnimation("Offset", offsetAnimation);
            }
            else
            {
                var cubicEaseIn = compositor.CreateCubicBezierEasingFunction(
                      new System.Numerics.Vector2(0.55f, 0.055f),
                      new System.Numerics.Vector2(0.675f, 0.19f)
                   );

                var saveBarHeight = SaveBarBorder.ActualHeight;

                var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();

                opacityAnimation.InsertKeyFrame(0f, 1f);
                opacityAnimation.InsertKeyFrame(1f, 0f, cubicEaseIn);

                opacityAnimation.Duration = animationDuration;

                saveBarVisual.StartAnimation("Opacity", opacityAnimation);

                var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();

                offsetAnimation.InsertKeyFrame(0f, Vector3.Zero);
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0, (float)saveBarHeight, 0), cubicEaseIn);

                offsetAnimation.Duration = animationDuration;

                bottomPanelVisual.StartAnimation("Offset", offsetAnimation);

                await Task.Delay(animationDuration);
                PrimaryInfoBar.CornerRadius = new CornerRadius(8, 8, 8, 8);
            }
        }

        void TryCloseInfoBar()
        {
            if (!PrimaryInfoBar.IsClosable && PrimaryInfoBar.Title != "Generating")
            {
                PrimaryInfoBar.IsOpen = false;
                SaveBarBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
            }
        }

        private static bool AreSubjectsCorrect()
        {
            return AreSubjectsCorrect(WorkingPath);
        }

        private static bool AreSubjectsCorrect(string workingPath)
        {
            try
            {
                if(GetMaxSubdirectoryDepth(workingPath) == 2)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        static int GetMaxSubdirectoryDepth(string path, int currentDepth = 0)
        {       
            var subdirectories = Directory.GetDirectories(path);
            if (subdirectories.Length == 0)
                return currentDepth;

            return subdirectories.Max(subDir => GetMaxSubdirectoryDepth(subDir, currentDepth + 1));
        }

        private void TreeViewControl_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = (args.InvokedItem as TreeViewNode)!;

            var selectedNodesCount = ProgramConfig.SelectedNodes?.Count ?? 0;

            bool isMultiSelectScenario =
                selectedNodesCount > 1 ||
                (selectedNodesCount == 1 && IsCtrlHeld && TreeViewControl.SelectedNode != node);

            if (isMultiSelectScenario)
            {
                if (TreeViewControl.SelectedNode == node)
                {
                    var nodeTreeItem = (TreeItem)node.Content!;
                    nodeTreeItem.IsSelected = false;

                    ProgramConfig.SelectedNodes!.RemoveAt(
                        ProgramConfig.SelectedNodes.Count - 1);

                    var lastSelected = ProgramConfig.SelectedNodes.Last();

                    node = node.Parent.Children
                        .Cast<TreeViewNode>()
                        .First(n => ((TreeItem)n.Content!).Text == lastSelected);
                }

                TreeViewControl.SelectedNode = node;
                ChangeConfigPanelIfNecessary(node, true);
                WaitThenSelect(node);
                return;
            }

            if (TreeViewControl.SelectedNode == node)
            {
                TreeViewControl.SelectedNode = null;
            }
            else
            {
                TreeViewControl.SelectedNode = node;
                ChangeConfigPanelIfNecessary(node, true);
            }
        }

        async void WaitThenSelect(TreeViewNode node)
        {
            await Task.Yield();
            TreeViewControl.SelectedNode = node;
        }

        async void ChangeConfigPanelIfNecessary(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        { 
            SettingsToggleButton.IsChecked = false;

            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            bool sameDepth = false;
            if(ProgramConfig.SelectedNodes != null && (
               depth == ItemDepth.Subject && ProgramConfig.SelectedNodePath!.Count == 0 ||
               depth == ItemDepth.Animation && ProgramConfig.SelectedNodePath!.Count == 1 ||
               depth == ItemDepth.Frame && ProgramConfig.SelectedNodePath!.Count == 2))
            {
                sameDepth = true;
                animate = false;
            }

            FadeOutAllPanels(sameDepth, animate);
            ChangeConfigPanelAsync(node, animate, nowGenerated);
        }

        void FadeOutAllPanels(bool sameDepth, bool animate = true)
        {
            var panels = new[] { SubjectPanel, AnimationsPanel, FramePanel, HelpPanel };
            foreach (var panel in panels)
            {
                if (panel.Visibility == Visibility.Visible)
                {
                    if (animate)
                    {
                        FadeOutPanel(panel, sameDepth);
                    }
                    else if (!sameDepth)
                    {
                        panel.Visibility = Visibility.Collapsed;
                    }               
                }
            }
        }

        void FadeOutPanel(UIElement panel, bool sameDepth)
        {
            var storyboard = new Storyboard();
            var doubleAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(_fadeOutMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(doubleAnimation, panel);
            Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
            storyboard.Children.Add(doubleAnimation);
    
            storyboard.Completed += (s, e) =>
            {
                if (!sameDepth)
                {
                    panel.Visibility = Visibility.Collapsed;
                }
                panel.Opacity = 1.0;
            };

            storyboard.Begin();
        }

        void FadeInPanel(UIElement panel)
        {
            panel.Opacity = 0.0;
            panel.Visibility = Visibility.Visible;

            if (panel.RenderTransform is not TranslateTransform translateTransform)
            {
                translateTransform = new TranslateTransform();
                panel.RenderTransform = translateTransform;
            }

            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(_fadeInMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacityAnimation, panel);
            Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
            storyboard.Children.Add(opacityAnimation);

            var translateAnimation = new DoubleAnimation
            {
                From = 10.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(_fadeInMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateAnimation, translateTransform);
            Storyboard.SetTargetProperty(translateAnimation, "Y");
            storyboard.Children.Add(translateAnimation);

            storyboard.Begin();
        }

        void CheckFrameCountAndDisplayWarning(int count)
        {
            if (count <= 0)
            {
                SetInfoBar(InfoBarSeverity.Warning, "Animations are empty", "Add at least one frame to at least one animation.", false);
                IsEnoughFrames = false;
            }
            else
            {
                IsEnoughFrames = true;
                TryCloseInfoBar();
            }
        }

        void HandleSelection(TreeItem selectedNode, bool nowGenerated, List<string> newSelectedNodePath)
        {
            bool inSamePath = ProgramConfig.SelectedNodePath!.SequenceEqual(newSelectedNodePath);
            if ((!inSamePath || !IsCtrlHeld) && !nowGenerated)
            {
                ProgramConfig.SelectedNodes = [];
                ClearAllTreeItemSelections();
            }

            ProgramConfig.SelectedNodePath = newSelectedNodePath;
            ProgramConfig.SelectedNodes!.Remove(selectedNode.Text);
            ProgramConfig.SelectedNodes.Add(selectedNode.Text);
            RemoveMovementButton.IsEnabled = ProgramConfig.SelectedNodes.Count > 1;
            selectedNode.IsSelected = true;
        }

        async void ChangeConfigPanelAsync(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {

            IsPanelChangeInProgress = true;
            UIElement panelToShow = HelpPanel;

            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            AnimateGeneratePanel(show: true);
            var selectedNode = (node.Content as TreeItem)!;

            SettingsToggleButton.IsChecked = false;
            switch (depth)
            {
                case ItemDepth.Subject:
                    panelToShow = SubjectPanel;  
                    var subjectName = (node.Content as TreeItem)!.Text;
                    HandleSelection(selectedNode, nowGenerated, []);
                    UpdateBreadcrumb(string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
                    CheckFrameCountAndDisplayWarning((node.Content as TreeItem)!.Count);

                    GenerateButton.Content = $"Generate {selectedNode.Text}";

                    _currentConfigs = [];
                    foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
                    {
                        _currentConfigs.Add(ProgramConfig.AssetConfig!.SubjectConfigs![selectedNodeName]);
                    }
                    var subjectConfig = ProgramConfig.AssetConfig!.SubjectConfigs![subjectName];
                    if (animate)
                    {
                        await Task.Delay(_fadeOutMs);
                        FadeInPanel(panelToShow);
                    }
                    else
                    {
                        panelToShow.Visibility = Visibility.Visible;
                    }
                    DisplaySubjectConfigAsync(subjectConfig);
                    break;

                case ItemDepth.Animation:
                    panelToShow = AnimationsPanel;
                    subjectName = (node.Parent.Content as TreeItem)!.Text;
                    string animationName = (node.Content as TreeItem)!.Text;
                    HandleSelection(selectedNode!, nowGenerated, [subjectName]);
                    UpdateBreadcrumb(subjectName, string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
                    CheckFrameCountAndDisplayWarning((node.Parent.Content as TreeItem)!.Count);

                    GenerateButton.Content = $"Generate {ProgramConfig.SelectedNodePath![0]}";

                    _currentConfigs = [];
                    foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
                    {
                        _currentConfigs.Add(ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![selectedNodeName]);
                    }

                    var animationConfig = ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName];
                    subjectConfig = ProgramConfig.AssetConfig!.SubjectConfigs![subjectName];
                    if (animate)
                    {
                        await Task.Delay(_fadeOutMs);
                        FadeInPanel(panelToShow);
                    }
                    else
                    {
  
                        panelToShow.Visibility = Visibility.Visible;
                    }
                    DisplayAnimationCongifAsync(subjectConfig, animationConfig);
                    break;

                case ItemDepth.Frame:
                    panelToShow = FramePanel;
                    subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
                    animationName = (node.Parent.Content as TreeItem)!.Text;
                    string frameName = (node.Content as TreeItem)!.Text;
                    HandleSelection(selectedNode, nowGenerated, [subjectName, animationName]);
                    UpdateBreadcrumb(subjectName, animationName, string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
                    CheckFrameCountAndDisplayWarning((node.Parent.Parent.Content as TreeItem)!.Count);

                    GenerateButton.Content = $"Generate {ProgramConfig.SelectedNodePath![0]}";

                    _currentConfigs = [];
                    foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
                    {
                        _currentConfigs.Add(ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[int.Parse(selectedNodeName)]);
                    }

                    subjectConfig = ProgramConfig.AssetConfig!.SubjectConfigs![subjectName];
                    animationConfig = subjectConfig.AnimationConfigs![animationName];
                    bool isFromFramePanel = (TreeViewControl.SelectedNode == null || (TreeViewControl.SelectedNode.Content as TreeItem)!.Depth == ItemDepth.Frame);
                    int frameCount = TreeViewControl.SelectedNode!.Parent.Children.Count;
                    if (animate)
                    {
                        await Task.Delay(_fadeOutMs);
                        FadeInPanel(panelToShow);
                    }
                    else
                    {
               
                        panelToShow.Visibility = Visibility.Visible;
                    }
                    DisplayFrameCongifAsync(subjectName, animationName, frameName, isFromFramePanel, frameCount, subjectConfig, animationConfig);               
                    break;

                default:
                    break;
            }

            IsPanelChangeInProgress = false;
        }

        void DisplaySubjectConfigAsync(SubjectConfig subjectConfig)
        {
            ProcessingCardControl.GetRemoveBackgroundCheckBox.Click -= ClickRemoveBackground;
            ProcessingCardControl.GetCropLeftCheckBox.Click -= ClickCropLeftCheckBox;
            ProcessingCardControl.GetCropTopCheckBox.Click -= ClickCropTopCheckBox;
            ProcessingCardControl.GetCropRightCheckBox.Click -= ClickCropRightCheckBox;
            ProcessingCardControl.GetCropBottomCheckBox.Click -= ClickCropBottomCheckBox;

            ProcessingCardControl.GetResizeTextBox.TextChanged -= ResizeTextBox_ValueChanged;
            ProcessingCardControl.GetSamplingComboBox.SelectionChanged -= SamplingComboBox_SelectionChanged;
            ProcessingCardControl.GetMipmapComboBox.SelectionChanged -= MipmapComboBox_SelectionChanged;
            ProcessingCardControl.GetColorTextBox.TextChanged -= ColorTextBox_TextChanged;
            ProcessingCardControl.GetThresholdTextBox.TextChanged -= ThresholdTextBox_ValueChanged;

            SheetWidthTextBox.TextChanged -= SheetWidthTextBox_ValueChanged;
            SheetHeightTextBox.TextChanged -= SheetHeightTextBox_ValueChanged;

            subjectConfig.Export ??= new SubjectExportConfig();
            subjectConfig.Processing ??= new ProcessingConfig();

            ProcessingCardControl.GetRemoveBackgroundCheckBox.IsChecked = subjectConfig.Processing.RemoveBackground;
            ProcessingCardControl.GetCropLeftCheckBox.IsChecked = subjectConfig.Processing.CropLeft;
            ProcessingCardControl.GetCropTopCheckBox.IsChecked = subjectConfig.Processing.CropTop;
            ProcessingCardControl.GetCropRightCheckBox.IsChecked = subjectConfig.Processing.CropRight;
            ProcessingCardControl.GetCropBottomCheckBox.IsChecked = subjectConfig.Processing.CropBottom;

            ProcessingCardControl.GetResizeTextBox.Text = subjectConfig.Processing.ResizeToPercent.ToString();
            ProcessingCardControl.GetSamplingComboBox.SelectedIndex = subjectConfig.Processing.FilterMode;
            ProcessingCardControl.GetMipmapComboBox.SelectedIndex = subjectConfig.Processing.MipmapMode;

            ProcessingCardControl.GetThresholdTextBox.Text = subjectConfig.Processing.ColorTreshold.ToString();
            SheetWidthTextBox.Text = subjectConfig.Export.Width.ToString();
            SheetHeightTextBox.Text = subjectConfig.Export.Height.ToString();

            ProcessingCardControl.GetColorTextBox.Text = subjectConfig.Processing.BackgroundColor ?? "";
            ProcessingCardControl.UpdateColorPreview();

            ProcessingCardControl.GetRemoveBackgroundCheckBox.Click += ClickRemoveBackground;
            ProcessingCardControl.GetCropLeftCheckBox.Click += ClickCropLeftCheckBox;
            ProcessingCardControl.GetCropTopCheckBox.Click += ClickCropTopCheckBox;
            ProcessingCardControl.GetCropRightCheckBox.Click += ClickCropRightCheckBox;
            ProcessingCardControl.GetCropBottomCheckBox.Click += ClickCropBottomCheckBox;

            ProcessingCardControl.GetResizeTextBox.TextChanged += ResizeTextBox_ValueChanged;
            ProcessingCardControl.GetSamplingComboBox.SelectionChanged += SamplingComboBox_SelectionChanged;
            ProcessingCardControl.GetMipmapComboBox.SelectionChanged += MipmapComboBox_SelectionChanged;
            ProcessingCardControl.GetColorTextBox.TextChanged += ColorTextBox_TextChanged;
            ProcessingCardControl.GetThresholdTextBox.TextChanged += ThresholdTextBox_ValueChanged;

            SheetWidthTextBox.TextChanged += SheetWidthTextBox_ValueChanged;
            SheetHeightTextBox.TextChanged += SheetHeightTextBox_ValueChanged;

            SetCheckedState();
        }

        void DisplayAnimationCongifAsync(SubjectConfig subjectConfig, AnimationConfig animationConfig)
        {
            RegenerateCheckBox.Click -= ClickRegenerateCheckBox;
    
            RecoverXCheckBox.Click -= ClickRecoverXCheckBox;
            RecoverYCheckBox.Click -= ClickRecoverYCheckBox;

            DelayTextBox.TextChanged -= DelayTextBox_ValueChanged;
            LoopTypeComboBox.SelectionChanged -= LoopTypeComboBox_SelectionChanged;
            SkipTextBox.TextChanged -= SkipTextBox_ValueChanged;

            AnimationOffsetXTextBox.TextChanged -= AnimationOffsetXTextBox_ValueChanged;
            AnimationOffsetYTextBox.TextChanged -= AnimationOffsetYTextBox_ValueChanged;

            AlsoKnownAsTextBox.TextChanged -= AlsoKnownAsTextBox_TextChanged;
            AlsoKnownAsAddButton.Click -= AlsoKnownAsAddButton_Click;

            animationConfig.RecoverCroppedOffset ??= new RecoverCroppedOffset();
            animationConfig.Offset ??= new Vector2(0, 0);

            RegenerateCheckBox.IsChecked = animationConfig.Regenerate;
      
            RecoverXCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.X;
            RecoverYCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.Y;

            DelayTextBox.Text = animationConfig.Delay.ToString();

            LoopTypeComboBox.SelectedIndex = animationConfig.LoopType;

            SkipTextBox.Text = animationConfig.Skip.ToString();

            AnimationOffsetXTextBox.Text = animationConfig.Offset.Value.X.ToString();
            AnimationOffsetYTextBox.Text = animationConfig.Offset.Value.Y.ToString();

            AlsoKnownAsTextBox.Text = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!.AlsoKnownAs;

            RegenerateCheckBox.Click += ClickRegenerateCheckBox;
          
            RecoverXCheckBox.Click += ClickRecoverXCheckBox;
            RecoverYCheckBox.Click += ClickRecoverYCheckBox;

            DelayTextBox.TextChanged += DelayTextBox_ValueChanged;
            LoopTypeComboBox.SelectionChanged += LoopTypeComboBox_SelectionChanged;

            SkipTextBox.TextChanged += SkipTextBox_ValueChanged;

            AnimationOffsetXTextBox.TextChanged += AnimationOffsetXTextBox_ValueChanged;
            AnimationOffsetYTextBox.TextChanged += AnimationOffsetYTextBox_ValueChanged;

            AlsoKnownAsTextBox.TextChanged += AlsoKnownAsTextBox_TextChanged;    
            AlsoKnownAsAddButton.Click += AlsoKnownAsAddButton_Click;

            PopulateAlsoKnownAsList();
            AlsoKnownAsListView.ItemsSource = AlsoKnownAsEntries;
            AlsoKnownAsAddButton.IsEnabled = false;



            ProcessingConfig configToSetFrom;

            if (animationConfig.ProcessingOverwrite != null)
            {
                configToSetFrom = animationConfig.ProcessingOverwrite;
                RemoveOverwriteButton.Visibility = Visibility.Visible;
            }
            else
            {
                configToSetFrom = subjectConfig.Processing;
                RemoveOverwriteButton.Visibility = Visibility.Collapsed;
            }

            UpdateOverwriteUI(configToSetFrom);
        }

        void UpdateOverwriteUI(ProcessingConfig configToSetFrom)
        {

            ProcessingOverwriteCardControl.GetRemoveBackgroundCheckBox.Click -= ClickOverwriteRemoveBackground;
            ProcessingOverwriteCardControl.GetCropLeftCheckBox.Click -= ClickOverwriteCropLeftCheckBox;
            ProcessingOverwriteCardControl.GetCropTopCheckBox.Click -= ClickOverwriteCropTopCheckBox;
            ProcessingOverwriteCardControl.GetCropRightCheckBox.Click -= ClickOverwriteCropRightCheckBox;
            ProcessingOverwriteCardControl.GetCropBottomCheckBox.Click -= ClickOverwriteCropBottomCheckBox;

            ProcessingOverwriteCardControl.GetResizeTextBox.TextChanged -= ResizeOverwriteTextBox_ValueChanged;
            ProcessingOverwriteCardControl.GetSamplingComboBox.SelectionChanged -= SamplingOverwriteComboBox_SelectionChanged;
            ProcessingOverwriteCardControl.GetMipmapComboBox.SelectionChanged -= MipmapOverwriteComboBox_SelectionChanged;
            ProcessingOverwriteCardControl.GetColorTextBox.TextChanged -= ColorOverwriteTextBox_TextChanged;
            ProcessingOverwriteCardControl.GetThresholdTextBox.TextChanged -= ThresholdOverwriteTextBox_ValueChanged;

            ProcessingOverwriteCardControl.GetColorTextBox.TextChanged -= ProcessingOverwriteCardControl.ColorTextBox_TextChanged;

            ProcessingOverwriteCardControl.GetRemoveBackgroundCheckBox.IsChecked = configToSetFrom.RemoveBackground;
            ProcessingOverwriteCardControl.GetCropLeftCheckBox.IsChecked = configToSetFrom.CropLeft;
            ProcessingOverwriteCardControl.GetCropTopCheckBox.IsChecked = configToSetFrom.CropTop;
            ProcessingOverwriteCardControl.GetCropRightCheckBox.IsChecked = configToSetFrom.CropRight;
            ProcessingOverwriteCardControl.GetCropBottomCheckBox.IsChecked = configToSetFrom.CropBottom;

            ProcessingOverwriteCardControl.GetResizeTextBox.Text = configToSetFrom.ResizeToPercent.ToString();
            ProcessingOverwriteCardControl.GetSamplingComboBox.SelectedIndex = configToSetFrom.FilterMode;
            ProcessingOverwriteCardControl.GetMipmapComboBox.SelectedIndex = configToSetFrom.MipmapMode;
            ProcessingOverwriteCardControl.GetThresholdTextBox.Text = configToSetFrom.ColorTreshold.ToString();
            ProcessingOverwriteCardControl.GetColorTextBox.Text = configToSetFrom.BackgroundColor ?? "";
            ProcessingOverwriteCardControl.UpdateColorPreview();


            ProcessingOverwriteCardControl.GetRemoveBackgroundCheckBox.Click += ClickOverwriteRemoveBackground;
            ProcessingOverwriteCardControl.GetCropLeftCheckBox.Click += ClickOverwriteCropLeftCheckBox;
            ProcessingOverwriteCardControl.GetCropTopCheckBox.Click += ClickOverwriteCropTopCheckBox;
            ProcessingOverwriteCardControl.GetCropRightCheckBox.Click += ClickOverwriteCropRightCheckBox;
            ProcessingOverwriteCardControl.GetCropBottomCheckBox.Click += ClickOverwriteCropBottomCheckBox;

            ProcessingOverwriteCardControl.GetResizeTextBox.TextChanged += ResizeOverwriteTextBox_ValueChanged;
            ProcessingOverwriteCardControl.GetSamplingComboBox.SelectionChanged += SamplingOverwriteComboBox_SelectionChanged;
            ProcessingOverwriteCardControl.GetMipmapComboBox.SelectionChanged += MipmapOverwriteComboBox_SelectionChanged;
            ProcessingOverwriteCardControl.GetColorTextBox.TextChanged += ColorOverwriteTextBox_TextChanged;
            ProcessingOverwriteCardControl.GetThresholdTextBox.TextChanged += ThresholdOverwriteTextBox_ValueChanged;

            ProcessingOverwriteCardControl.GetColorTextBox.TextChanged += ProcessingOverwriteCardControl.ColorTextBox_TextChanged;

            SetOverwriteCheckedState();
        }

        CancellationTokenSource? cts= null;

        async void DisplayFrameCongifAsync(string subjectName, string animationName, string frameName, bool isFromFramePanel, int frameCount, SubjectConfig subjectConfig, AnimationConfig animationConfig)
        {

            DirectionTextBox.TextChanged -= DirectionTextBox_ValueChanged;
            SpeedTextBox.TextChanged -= SpeedTextBox_ValueChanged;

            BasedOnRadioButtons.SelectionChanged -= BasedOnRadioButtons_SelectionChanged;
            AlignOnXAxis.Click -= AlignOnXAxis_Click;
            AlignOnYAxis.Click -= AlignOnYAxis_Click;

            OffsetXTextBox.TextChanged -= OffsetXTextBox_ValueChanged;
            OffsetYTextBox.TextChanged -= OffsetYTextBox_ValueChanged;
            MultiplyTextBox.TextChanged -= MultiplyTextBox_ValueChanged;
       
            string[] newPath = [subjectName, animationName];

            bool subjectEquals = (AnimationSpriteFramePath[0] == newPath[0]);
            bool animationEquals = (subjectEquals && AnimationSpriteFramePath[1] == newPath[1]);
            bool isCancelled = false;
            if (!animationEquals)
            {
                if (!subjectEquals || (TreeViewControl.SelectedNode == null || !isFromFramePanel && animationName != AnimationSpriteFramePath[1]))
                {
                    FrameCoordinateEditorControl.UnloadAnimation();
                }
                FrameCoordinateEditorControl.PreviewSpriteFrames = [];
                AnimationSpriteFramePath = newPath;
                cts?.Cancel();
                isCancelled = true;
            }


            int selectedIndex = int.Parse(frameName);
            var frameConfig = animationConfig.FrameCongfigs[selectedIndex];

            AnimationInterfaceConfig animationInterfaceConfig = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!;

            DirectionTextBox.Text = animationInterfaceConfig.Direction.ToString();
            SpeedTextBox.Text = animationInterfaceConfig.Speed.ToString();

            BasedOnRadioButtons.SelectedIndex = (int)animationInterfaceConfig.AlignBasedOn;
            AlignOnXAxis.IsChecked = animationInterfaceConfig.AlignOnXAxis;
            AlignOnYAxis.IsChecked = animationInterfaceConfig.AlignOnYAxis;

            OffsetXTextBox.Text = frameConfig.Offset.X.ToString();
            OffsetYTextBox.Text = frameConfig.Offset.Y.ToString();
            MultiplyTextBox.Text = frameConfig.MultipyDelayBy.ToString();

            DirectionTextBox.TextChanged += DirectionTextBox_ValueChanged;
            SpeedTextBox.TextChanged += SpeedTextBox_ValueChanged;

            BasedOnRadioButtons.SelectionChanged += BasedOnRadioButtons_SelectionChanged;
            AlignOnXAxis.Click += AlignOnXAxis_Click;
            AlignOnYAxis.Click += AlignOnYAxis_Click;

            OffsetXTextBox.TextChanged += OffsetXTextBox_ValueChanged;
            OffsetYTextBox.TextChanged += OffsetYTextBox_ValueChanged;
            MultiplyTextBox.TextChanged += MultiplyTextBox_ValueChanged;
            if (IsLoadingFrames && !isCancelled) return;
            cts = new();
            try
            {
                await LoadCoordinateEditorAsync(subjectName, animationName, subjectConfig, frameName, selectedIndex, frameConfig, frameCount, cts.Token);
            }
            catch
            {
                IsLoadingFrames = false;
            }
            finally
            {
                cts = null;
            }
        }

        async Task LoadCoordinateEditorAsync(string subjectName, string animationName, SubjectConfig subjectConfig, string frameName, int selectedIndex, FrameConfig frameConfig, int frameCount, CancellationToken ct)
        {
            FrameCoordinateEditorControl.SpritePositionMoved -= SpriteOffset_ValueMoved;
            string animationPath = Path.Combine(WorkingPath, subjectName, animationName);
            var animationConfig = subjectConfig.AnimationConfigs![animationName];
            if (FrameCoordinateEditorControl.PreviewSpriteFrames.Count == 0)
            {
                IsLoadingFrames = true;

                List<SpriteFrame> tempAnimationSpriteFrames = new([]);

                ProcessingConfig processingConfig;
                if(animationConfig.ProcessingOverwrite != null)
                {
                    processingConfig = animationConfig.ProcessingOverwrite;
                }
                else
                {
                    processingConfig = subjectConfig.Processing;
                }

                ColorHelper.TryParse(processingConfig.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                SKColor backgroundSKColor = new(r, g, b, a);
          
                for (int i = 0; i < frameCount; i++)         
                {
                    FrameConfig frameConfigInLoop = animationConfig.FrameCongfigs[i];

                    string framePath = Path.Combine(animationPath, $"{frameConfigInLoop.Name}.png");

                    SpriteFrame spriteFrame = await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();

                        using var stream = File.OpenRead(framePath);
                        using var codec = SKCodec.Create(stream);
                
                        var desiredInfo = new SKImageInfo(
                            width: codec.Info.Width,
                            height: codec.Info.Height,
                            colorType: codec.Info.ColorType,
                            alphaType: SKAlphaType.Unpremul
                        );
                
                        var skb = SKBitmap.Decode(codec, desiredInfo);

                        IntVector2 originalSize = new(skb.Width, skb.Height);

                        if (backgroundSKColor.Alpha != 0 && processingConfig.RemoveBackground)
                            ColorHelper.RemoveColorWithThresholdInPlace(skb, backgroundSKColor.Red, backgroundSKColor.Green, backgroundSKColor.Blue, backgroundSKColor.Alpha, processingConfig.ColorTreshold);

                        var (left, top, right, bottom) = ColorHelper.RectTrimColor(skb, subjectConfig, (backgroundSKColor.Red, backgroundSKColor.Green, backgroundSKColor.Blue, backgroundSKColor.Alpha));
                        SKRectI rect = new(left, top, right, bottom);

                        bool isSame = (left == 0 && top == 0 && right == skb.Width && bottom == skb.Height);
                        if ((processingConfig.CropLeft || processingConfig.CropTop || processingConfig.CropRight || processingConfig.CropBottom || processingConfig.RemoveBackground || backgroundSKColor.Alpha == 0) && !isSame)
                        {                  
                            var width = right - left;
                            var height = bottom - top;
                            var cropped = new SKBitmap(new SKImageInfo(width, height, skb.ColorType, skb.AlphaType));
                            using (var canvas = new SKCanvas(cropped))
                            {
                                canvas.Clear(SKColors.Transparent);
                                var sourceRect = new SKRect(left, top, left + width, top + height);
                                var destRect = new SKRect(0, 0, width, height);
                                canvas.DrawBitmap(skb, sourceRect, destRect);
                            }              
                            return new SpriteFrame(cropped, rect, originalSize);
                        }
                        return new SpriteFrame(skb, rect, originalSize);

                    }, ct);

                    if (spriteFrame == null)
                        throw new Exception($"Failed to decode image: {framePath}");

                    tempAnimationSpriteFrames.Add(spriteFrame);
                    ct.ThrowIfCancellationRequested();
                }


                var selectedNodeAfter = TreeViewControl.SelectedNode;
                if (selectedNodeAfter != null)
                {
                    var selectedNodeAfterContent = selectedNodeAfter.Content as TreeItem;
                    if (selectedNodeAfterContent!.Depth == ItemDepth.Frame)
                    {
                        frameName = selectedNodeAfterContent.Text;
                        selectedIndex = int.Parse(frameName);
                        frameConfig = animationConfig.FrameCongfigs[selectedIndex];
                    }
                }

                IsLoadingFrames = false;
                FrameCoordinateEditorControl.LoadAnimation(tempAnimationSpriteFrames, subjectConfig, animationName, backgroundSKColor);
            }
            
            FrameCoordinateEditorControl.SetSpriteIndex(selectedIndex);
            FrameCoordinateEditorControl.SpritePositionMoved += SpriteOffset_ValueMoved;

            if (IsWindowActive)
            {
                SyncKeyboardState();
            }                  
        }

        FrameConfig GetCurrentFrameConfig()
        {
            var subjectName = (TreeViewControl.SelectedNode.Parent.Parent.Content as TreeItem)!.Text;
            var animationName = (TreeViewControl.SelectedNode.Parent.Content as TreeItem)!.Text;
            string frameName = (TreeViewControl.SelectedNode.Content as TreeItem)!.Text;
            return ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[int.Parse(frameName)];
        }

        AnimationConfig GetCurrentFrameAnimationConfig()
        {
            var node = TreeViewControl.SelectedNode;
            var subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
            var animationName = (node.Parent.Content as TreeItem)!.Text;
            return ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName];
        }

        AnimationConfig GetCurrentAnimationConfig()
        {
            var node = TreeViewControl.SelectedNode;
            var subjectName = (node.Parent.Content as TreeItem)!.Text;
            var animationName = (node.Content as TreeItem)!.Text;
            return ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName];
        }

        SubjectConfig GetCurrentAnimationSubjectConfig()
        {
            var node = TreeViewControl.SelectedNode;
            var subjectName = (node.Parent.Content as TreeItem)!.Text;
            return ProgramConfig.AssetConfig!.SubjectConfigs![subjectName];
        }

        SubjectConfig GetCurrentSubjectConfig()
        {
            var node = TreeViewControl.SelectedNode;
            var subjectName = (node.Parent.Content as TreeItem)!.Text;
            return ProgramConfig.AssetConfig!.SubjectConfigs![subjectName];
        }

        public void RefreshOffsetFieldVisually()
        {
            OffsetXTextBox.TextChanged -= OffsetXTextBox_ValueChanged;
            OffsetYTextBox.TextChanged -= OffsetYTextBox_ValueChanged;

            var frameConfig = GetCurrentFrameConfig();

            OffsetXTextBox.Text = frameConfig.Offset.X.ToString();
            OffsetYTextBox.Text = frameConfig.Offset.Y.ToString();

            OffsetXTextBox.TextChanged += OffsetXTextBox_ValueChanged;
            OffsetYTextBox.TextChanged += OffsetYTextBox_ValueChanged;
        }
        private void RemoveMovementButton_Click(object sender, RoutedEventArgs e)
        {
            var animationNode = TreeViewControl.SelectedNode.Parent;

            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            AnimationInterfaceConfig animationInterfaceConfig = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!;
            List<FrameConfig> frameConfigList = animationConfig.FrameCongfigs;

            Vector2? initialPosition = null;
            for (int i = 0; i < animationNode.Children.Count; i++)
            {
                var frameNodeContent = (animationNode.Children[i].Content as TreeItem)!;
                if (frameNodeContent.IsSelected)
                {
                    if (initialPosition == null)
                    {
                        initialPosition = new(frameConfigList[i].Offset.X, frameConfigList[i].Offset.Y);
                    }
                    else
                    {
                        initialPosition -= ConvertToVector2(animationInterfaceConfig.Direction, animationInterfaceConfig.Speed);
                        frameConfigList[i].Offset = new((int)Math.Round(initialPosition.Value.X), (int)Math.Round(initialPosition.Value.Y));
                    }
                }
            }
            if ((TreeViewControl.SelectedNode.Content as TreeItem)!.Text != "000")
            {
                RefreshOffsetFieldVisually();
            }
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        public Vector2 ConvertToVector2(double angleInDegrees, float distance)
        {
            double radians = (angleInDegrees * Math.PI) / 180.0;

            float x = (float)(Math.Sin(radians) * distance);
            float y = (float)(Math.Cos(radians) * distance);

            return new Vector2(x, y);
        }

        private void ALignDownButton_Click(object sender, RoutedEventArgs e)
        {
            int rawPositionX = (FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.X / 2) * -1;
            int rawPositionY = FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.Y;
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            AnimationInterfaceConfig animationInterfaceConfig = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!;
            FrameConfig frameConfig = animationConfig.FrameCongfigs[int.Parse((TreeViewControl.SelectedNode.Content as TreeItem)!.Text)];

            if (animationInterfaceConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                int x = frameConfig.Offset.X;
                int y = frameConfig.Offset.Y;
                if (animationInterfaceConfig.AlignOnXAxis)
                    x = rawPositionX;
                if (animationInterfaceConfig.AlignOnYAxis)
                    y = rawPositionY;
                SpriteOffset_ValueChanged(new(x, y));
            }
            else
            {
                List<FrameConfig> frameConfigList = animationConfig.FrameCongfigs;
                var animationNode = TreeViewControl.SelectedNode.Parent;
                for (int i = 0; i < animationNode.Children.Count; i++)
                {
                    var frameNodeContent = (animationNode.Children[i].Content as TreeItem)!;
                    if (frameNodeContent.IsSelected)
                    {
                        int x = animationConfig.FrameCongfigs[i].Offset.X;
                        int y = animationConfig.FrameCongfigs[i].Offset.Y;
                        if (animationInterfaceConfig.AlignOnXAxis)
                            x = rawPositionX + ((FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.X - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Width) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Left;
                        if (animationInterfaceConfig.AlignOnYAxis)
                            y = rawPositionY - (FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.Y - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Bottom);     
                        
                        frameConfigList[i].Offset = new(x, y);
                    }
                }
            }

            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
        {
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            FrameConfig frameConfig = animationConfig.FrameCongfigs[int.Parse((TreeViewControl.SelectedNode.Content as TreeItem)!.Text)];
            AnimationInterfaceConfig animationInterfaceConfig = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!;

            if (animationInterfaceConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                int x = frameConfig.Offset.X;
                int y = frameConfig.Offset.Y;
                if (animationInterfaceConfig.AlignOnXAxis)
                    x = 0;
                if (animationInterfaceConfig.AlignOnYAxis)
                    y = 0;
                SpriteOffset_ValueChanged(new(x, y));
            }
            else
            {
                List<FrameConfig> frameConfigList = animationConfig.FrameCongfigs;
                var animationNode = TreeViewControl.SelectedNode.Parent;
                for (int i = 0; i < animationNode.Children.Count; i++)
                {
                    var frameNodeContent = (animationNode.Children[i].Content as TreeItem)!;
                    if (frameNodeContent.IsSelected)
                    {
                        int x = animationConfig.FrameCongfigs[i].Offset.X;
                        int y = animationConfig.FrameCongfigs[i].Offset.Y;
                        if (animationInterfaceConfig.AlignOnXAxis)
                            x = 0 - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Left;
                        if (animationInterfaceConfig.AlignOnYAxis)
                            y = 0 + FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Top;

                        frameConfigList[i].Offset = new(x, y);

                    }
                }
            }
            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void ALignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            int rawPositionX = (FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.X / 2) * -1;
            int rawPositionY = FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.Y / 2;
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            AnimationInterfaceConfig animationInterfaceConfig = (animationConfig.InterfaceConfig as AnimationInterfaceConfig)!;
            FrameConfig frameConfig = animationConfig.FrameCongfigs[int.Parse((TreeViewControl.SelectedNode.Content as TreeItem)!.Text)];

            if (animationInterfaceConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                int x = frameConfig.Offset.X;
                int y = frameConfig.Offset.Y;
                if(animationInterfaceConfig.AlignOnXAxis)
                    x = rawPositionX;
                if (animationInterfaceConfig.AlignOnYAxis)
                    y = rawPositionY;
                SpriteOffset_ValueChanged(new(x, y));
            }
            else
            {
                List<FrameConfig> frameConfigList = animationConfig.FrameCongfigs;
                var animationNode = TreeViewControl.SelectedNode.Parent;
                for (int i = 0; i < animationNode.Children.Count; i++)
                {
                    var frameNodeContent = (animationNode.Children[i].Content as TreeItem)!;
                    if (frameNodeContent.IsSelected)
                    {
                        int x = animationConfig.FrameCongfigs[i].Offset.X;
                        int y = animationConfig.FrameCongfigs[i].Offset.Y;

                        if (animationInterfaceConfig.AlignOnXAxis)
                            x = rawPositionX + ((FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.X - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Width) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Left;
                        if (animationInterfaceConfig.AlignOnYAxis)
                            y = rawPositionY + (((FrameCoordinateEditorControl.PreviewSpriteFrames[0].OriginalSize.Y - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Height) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames[i].CroppedRect.Top) * -1;

                        frameConfigList[i].Offset = new(x, y);
                    }
                }
            }
            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void OffsetXTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            SpriteOffset_ValueChanged(new(string.IsNullOrWhiteSpace(text) ? 0 : int.Parse(text), GetCurrentFrameConfig().Offset.Y));
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void OffsetYTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            SpriteOffset_ValueChanged(new IntVector2(GetCurrentFrameConfig().Offset.X, string.IsNullOrWhiteSpace(text) ? 0 : int.Parse(text)));
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void MultiplyTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            GetCurrentFrameConfig().MultipyDelayBy = string.IsNullOrWhiteSpace(text) ? 1 : int.Parse(text);
        }

        private AnimationInterfaceConfig GetCurrentFrameAnimationInterfaceConfig()
        {
            return (GetCurrentFrameAnimationConfig().InterfaceConfig as AnimationInterfaceConfig)!;
        }

        private void BasedOnRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GetCurrentFrameAnimationInterfaceConfig().AlignBasedOn = (AlignBasedOn)(sender as RadioButtons)!.SelectedIndex;
        }

        private void AlignOnXAxis_Click(object sender, RoutedEventArgs e)
        {
            GetCurrentFrameAnimationInterfaceConfig().AlignOnXAxis = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void AlignOnYAxis_Click(object sender, RoutedEventArgs e)
        {
            GetCurrentFrameAnimationInterfaceConfig().AlignOnYAxis = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void DirectionTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            GetCurrentFrameAnimationInterfaceConfig().Direction = string.IsNullOrWhiteSpace(text) ? 90 : float.Parse(text);
        }

        private void SpeedTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            GetCurrentFrameAnimationInterfaceConfig().Speed = string.IsNullOrWhiteSpace(text) ? 0 : float.Parse(text);
        }

        private void SpriteOffset_ValueChanged(IntVector2 intVector2)
        {
            foreach (FrameConfig currentConfig in _currentConfigs)
            {
                currentConfig.Offset = intVector2;
            }
        }

        private void SpriteOffset_ValueMoved(IntVector2 intVector2)
        {
            foreach (FrameConfig currentConfig in _currentConfigs)
            {
                currentConfig.Offset = new(currentConfig.Offset.X + intVector2.X, currentConfig.Offset.Y + intVector2.Y);
            }
            RefreshOffsetFieldVisually();
        }

        private void AnimationOffsetYTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig!.Offset = new Vector2((currentConfig).Offset!.Value.X, string.IsNullOrWhiteSpace(text) ? 0 : float.Parse(text));
            }         
        }

        private void SkipTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Skip = int.Parse(text);
            }
        }

        private void AnimationOffsetXTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Offset = new Vector2(string.IsNullOrWhiteSpace(text) ? 0 : float.Parse(text), (currentConfig).Offset!.Value.Y);
            }
        }

        private void DelayTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Delay = string.IsNullOrWhiteSpace(text) ? 1 : int.Parse(text);
            }
        }

        private void LoopTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.LoopType = (sender as ComboBox).SelectedIndex;
            }
        }

        private void SheetHeightTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Export.Height = string.IsNullOrWhiteSpace(text) ? null : int.Parse(text);
            }
        }

        private void SheetWidthTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Export.Width = string.IsNullOrWhiteSpace(text) ? null : int.Parse(text);
            }
        }

        private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

            string backgroundColor = (sender as TextBox)!.Text;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.BackgroundColor = backgroundColor;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ThresholdTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            int threshold = string.IsNullOrWhiteSpace(text) ? 100 : int.Parse(text);
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.ColorTreshold = threshold;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ClickRemoveBackground(object sender, RoutedEventArgs e)
        {
            bool removeBackground = (sender as CheckBox)!.IsChecked!.Value;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.RemoveBackground = removeBackground;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ResizeTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.ResizeToPercent = string.IsNullOrWhiteSpace(text) ? 100 : float.Parse(text);
            }
        }

        private void SamplingComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.FilterMode = (sender as ComboBox)!.SelectedIndex;
            }
        }

        private void MipmapComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.MipmapMode = (sender as ComboBox)!.SelectedIndex;
            }
        }

        private void ClickCropLeftCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.CropLeft = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetCheckedState();
        }

        private void ClickCropTopCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.CropTop = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetCheckedState();
        }

        private void ClickCropRightCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.CropRight = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetCheckedState();
        }

        private void ClickCropBottomCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.CropBottom = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetCheckedState();
        }



        private void ClickRecoverYCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.RecoverCroppedOffset.Y = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickRecoverXCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.RecoverCroppedOffset.X = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickRegenerateCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Regenerate = (sender as CheckBox)!.IsChecked!.Value;
            }
        }


        private void AlsoKnownAsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var subjConf = GetCurrentSubjectConfig();
            string text = (sender as TextBox)!.Text;
            bool isAllowedToAdd = !string.IsNullOrWhiteSpace(text);

            foreach ((string animationName, AnimationConfig animationConfig) in subjConf.AnimationConfigs!)
            {
                if (animationName == text || (animationConfig.AlsoKnownAs != null && animationConfig.AlsoKnownAs.ContainsKey(text)))
                    isAllowedToAdd = false;
            }

            AlsoKnownAsAddButton.IsEnabled = isAllowedToAdd;

            var animConf = subjConf.AnimationConfigs[(TreeViewControl.SelectedNode.Content as TreeItem)!.Text];
            (animConf.InterfaceConfig as AnimationInterfaceConfig)!.AlsoKnownAs = text;  
        }

    

        private void ColorOverwriteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

            string backgroundColor = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.BackgroundColor = backgroundColor??"";
            }
            AnimationSpriteFramePath = new string[3];
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ThresholdOverwriteTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            int threshold = string.IsNullOrWhiteSpace(text) ? 100 : int.Parse(text);
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.ColorTreshold = threshold;
            }
            AnimationSpriteFramePath = new string[3];
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ClickOverwriteRemoveBackground(object sender, RoutedEventArgs e)
        {
            bool removeBackground = (sender as CheckBox)!.IsChecked!.Value;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.RemoveBackground = removeBackground;
            }
            AnimationSpriteFramePath = new string[3];
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ResizeOverwriteTextBox_ValueChanged(object sender, RoutedEventArgs args)
        {
            string text = (sender as TextBox)!.Text;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.ResizeToPercent = string.IsNullOrWhiteSpace(text) ? 100 : float.Parse(text);
            }
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void SamplingOverwriteComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.FilterMode = (sender as ComboBox)!.SelectedIndex;
            }
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void MipmapOverwriteComboBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.MipmapMode = (sender as ComboBox)!.SelectedIndex;
            }
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ClickOverwriteCropLeftCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.CropLeft = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetOverwriteCheckedState();
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ClickOverwriteCropTopCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.CropTop = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetOverwriteCheckedState();
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ClickOverwriteCropRightCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.CropRight = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetOverwriteCheckedState();
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void ClickOverwriteCropBottomCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.CropBottom = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
            SetOverwriteCheckedState();
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        void SetEveryCrop(bool isChecked)
        {
            ProcessingCardControl.GetCropLeftCheckBox.IsChecked = ProcessingCardControl.GetCropTopCheckBox.IsChecked = ProcessingCardControl.GetCropRightCheckBox.IsChecked = ProcessingCardControl.GetCropBottomCheckBox.IsChecked = isChecked;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Processing.CropLeft = isChecked;
                currentConfig.Processing.CropTop = isChecked;
                currentConfig.Processing.CropRight = isChecked;
                currentConfig.Processing.CropBottom = isChecked;
            }
            AnimationSpriteFramePath = new string[3];
        }

        void SetEveryOverwriteCrop(bool isChecked)
        {
            ProcessingOverwriteCardControl.GetCropLeftCheckBox.IsChecked = ProcessingOverwriteCardControl.GetCropTopCheckBox.IsChecked = ProcessingOverwriteCardControl.GetCropRightCheckBox.IsChecked = ProcessingOverwriteCardControl.GetCropBottomCheckBox.IsChecked = isChecked;
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite ??= (ProcessingConfig)GetCurrentAnimationSubjectConfig().Processing.Clone();
                currentConfig.ProcessingOverwrite.CropLeft = isChecked;
                currentConfig.ProcessingOverwrite.CropTop = isChecked;
                currentConfig.ProcessingOverwrite.CropRight = isChecked;
                currentConfig.ProcessingOverwrite.CropBottom = isChecked;
            }
            AnimationSpriteFramePath = new string[3];
            RemoveOverwriteButton.Visibility = Visibility.Visible;
        }

        private void CropSpritesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = (sender as CheckBox)!;
            if (checkBox.IsChecked == null)
            {
                if (ProcessingCardControl.GetCropLeftCheckBox.IsChecked == true &&
                    ProcessingCardControl.GetCropTopCheckBox.IsChecked == true &&
                    ProcessingCardControl.GetCropRightCheckBox.IsChecked == true &&
                    ProcessingCardControl.GetCropBottomCheckBox.IsChecked == true)
                {
                    ProcessingCardControl.GetCropSpritesCheckBox.IsChecked = false;
                    SetEveryCrop(false);
                }
            }
            else
            {
                if (checkBox.IsChecked == true)
                {
                    SetEveryCrop(true);
                }
                else
                {
                    SetEveryCrop(false);
                }
            }
        }

        private void CropSpritesOverwriteCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = (sender as CheckBox)!;
            if (checkBox.IsChecked == null)
            {
                if (ProcessingOverwriteCardControl.GetCropLeftCheckBox.IsChecked == true &&
                    ProcessingOverwriteCardControl.GetCropTopCheckBox.IsChecked == true &&
                    ProcessingOverwriteCardControl.GetCropRightCheckBox.IsChecked == true &&
                    ProcessingOverwriteCardControl.GetCropBottomCheckBox.IsChecked == true)
                {
                    ProcessingOverwriteCardControl.GetCropSpritesCheckBox.IsChecked = false;
                    SetEveryOverwriteCrop(false);
                }
            }
            else
            {
                if (checkBox.IsChecked == true)
                {
                    SetEveryOverwriteCrop(true);
                }
                else
                {
                    SetEveryOverwriteCrop(false);
                }
            }
        }

        private void SetCheckedState()
        {     
            if (ProcessingCardControl.GetCropLeftCheckBox.IsChecked == true &&
                ProcessingCardControl.GetCropTopCheckBox.IsChecked == true &&
                ProcessingCardControl.GetCropRightCheckBox.IsChecked == true &&
                ProcessingCardControl.GetCropBottomCheckBox.IsChecked == true)
            {
                ProcessingCardControl.GetCropSpritesCheckBox.IsChecked = true;
            }
            else if (ProcessingCardControl.GetCropLeftCheckBox.IsChecked == false &&
                ProcessingCardControl.GetCropTopCheckBox.IsChecked == false &&
                ProcessingCardControl.GetCropRightCheckBox.IsChecked == false &&
                ProcessingCardControl.GetCropBottomCheckBox.IsChecked == false)
            {
                ProcessingCardControl.GetCropSpritesCheckBox.IsChecked = false;
            }
            else
            {
                ProcessingCardControl.GetCropSpritesCheckBox.IsChecked = null;
            }         
        }

        private void SetOverwriteCheckedState()
        {
            if (ProcessingOverwriteCardControl.GetCropLeftCheckBox.IsChecked == true &&
                ProcessingOverwriteCardControl.GetCropTopCheckBox.IsChecked == true &&
                ProcessingOverwriteCardControl.GetCropRightCheckBox.IsChecked == true &&
                ProcessingOverwriteCardControl.GetCropBottomCheckBox.IsChecked == true)
            {
                ProcessingOverwriteCardControl.GetCropSpritesCheckBox.IsChecked = true;
            }
            else if (ProcessingOverwriteCardControl.GetCropLeftCheckBox.IsChecked == false &&
                ProcessingOverwriteCardControl.GetCropTopCheckBox.IsChecked == false &&
                ProcessingOverwriteCardControl.GetCropRightCheckBox.IsChecked == false &&
                ProcessingOverwriteCardControl.GetCropBottomCheckBox.IsChecked == false)
            {
                ProcessingOverwriteCardControl.GetCropSpritesCheckBox.IsChecked = false;
            }
            else
            {
                ProcessingOverwriteCardControl.GetCropSpritesCheckBox.IsChecked = null;
            }
        }

        private void RemoveOverwriteButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.ProcessingOverwrite = null;
            }
            AnimationSpriteFramePath = new string[3];

            RemoveOverwriteButton.Visibility = Visibility.Collapsed;
        
            UpdateOverwriteUI(GetCurrentAnimationSubjectConfig().Processing);
    
        }

        private void MirrorButton_Click(object sender, RoutedEventArgs e)
        {
            var node = TreeViewControl.SelectedNode;
            var subjectName = (node.Parent.Content as TreeItem)!.Text;
            var animationName = (node.Content as TreeItem)!.Text;
            string animationPath = Path.Combine(WorkingPath, subjectName, animationName);
            var animationConfig = ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName];
            SetInfoBar(InfoBarSeverity.Informational, "Mirroring", $"{animationName} frames are being mirrored");
            try
            {
                for (int i = 0; i < animationConfig.FrameCongfigs.Count; i++)
                {
                    var frameConfig = animationConfig.FrameCongfigs[i];
                    string framePath = Path.Combine(animationPath, frameConfig.Name + ".png");
                    if (!File.Exists(framePath))
                        continue;

                    byte[] fileBytes = File.ReadAllBytes(framePath);

                    using var src = SKBitmap.Decode(fileBytes);
                    if (src == null)
                        continue;

                    var flipped = new SKBitmap(src.Info.Width, src.Info.Height, src.ColorType, src.AlphaType);
                    using (var canvas = new SKCanvas(flipped))
                    {
                        canvas.Scale(-1, 1);
                        canvas.Translate(-src.Width, 0);
                        canvas.DrawBitmap(src, 0, 0);
                        canvas.Flush();
                    }
   
                    using (var data = flipped.Encode(SKEncodedImageFormat.Png, 100))
                    using (var outStream = File.Open(framePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        data.SaveTo(outStream);
                    }
           
                    flipped.Dispose();                  
                }
                SetInfoBar(InfoBarSeverity.Success, "Mirrored", $"Mirrored every frame in {animationName}");        
            }
            catch (Exception ex)
            {
                SetInfoBar(InfoBarSeverity.Error, "Mirror failed", ex.Message);
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ClickIsHdCheckBox(object sender, RoutedEventArgs e)
        {
            ProgramConfig.AssetConfig!.IsHd = (sender as CheckBox)!.IsChecked!.Value;
        }

       
        private void TreeViewControl_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;
            ItemDepth depth = (node.Content as TreeItem)!.Depth;

            switch (depth)
            {
                case ItemDepth.Subject:
                    ProgramConfig.AssetConfig!.SubjectConfigs![(node.Content as TreeItem)!.Text].InterfaceConfig.IsExpanded = true;
                    break;
                case ItemDepth.Animation:
                    ProgramConfig.AssetConfig!.SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs![(node.Content as TreeItem)!.Text].InterfaceConfig.IsExpanded = true;
                    break;
                default:
                    break;
            }
        }

        private void TreeViewControl_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            TreeViewNode node = args.Node;
            ItemDepth depth = (node.Content as TreeItem)!.Depth;

            switch (depth)
            {
                case ItemDepth.Subject:
                    ProgramConfig.AssetConfig!.SubjectConfigs![(node.Content as TreeItem)!.Text].InterfaceConfig.IsExpanded = false;
                    break;
                case ItemDepth.Animation:
                    ProgramConfig.AssetConfig!.SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs![(node.Content as TreeItem)!.Text].InterfaceConfig.IsExpanded = false;
                    break;
                default:
                    break;
            }
        }

        private async void ClickSettings(object sender, RoutedEventArgs e)
        {   
            OpenSettingsAsync();
            IsPanelChangeInProgress = true;
            await Task.Delay(_fadeOutMs + _fadeInMs);
            IsPanelChangeInProgress = false;
        }

        async void OpenSettingsAsync()
        {
            SettingsToggleButton.IsChecked = true;
            ClearAllTreeItemSelections();
            
            if (HelpPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            TreeViewControl.SelectedNode = null;
            ProgramConfig.SelectedNodePath = [];
            ProgramConfig.SelectedNodes = null;
            FadeOutAllPanels(false, true);

            UpdateBreadcrumb("Settings & Help");
            AnimateGeneratePanel(show: false);
            TryCloseInfoBar();
       
            await Task.Delay(_fadeOutMs);

            if (TreeViewControl.SelectedNode != null)
            {
       
                return;
            }
            FadeInPanel(HelpPanel);
        }

        public void OpenSettingsAndHideGeneratePanelImmediately()
        {
            if (!_isActivated)
            {
                SaveBarBorder.Opacity = 0;

                BottomBarStackPanel.LayoutUpdated -= BottomBarStackPanel_LayoutUpdated;
                BottomBarStackPanel.LayoutUpdated += BottomBarStackPanel_LayoutUpdated;
            }

            OpenSettingsAsync();
        }

        private void BottomBarStackPanel_LayoutUpdated(object? sender, object e)
        {
            BottomBarStackPanel.LayoutUpdated -= BottomBarStackPanel_LayoutUpdated;

            var bottomPanelVisual = ElementCompositionPreview.GetElementVisual(BottomBarStackPanel);
            bottomPanelVisual.Offset = new Vector3(0, (float)SaveBarBorder.ActualHeight, 0);
        }

        private void SetInfoBar(string debug)
        {
            SetInfoBar(InfoBarSeverity.Informational, debug, "");
        }

        private void SetInfoBar(InfoBarSeverity severity, string title, string message, bool isClosable = true)
        {
            PrimaryInfoBar.Title = title;
            PrimaryInfoBar.Message = message;
            PrimaryInfoBar.Severity = severity;
            PrimaryInfoBar.IsClosable = isClosable;

            SaveBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            PrimaryInfoBar.IsOpen = true;

            switch (severity)
            {
                case InfoBarSeverity.Success:
                    SystemSounds.Asterisk.Play();
                    break;
                case InfoBarSeverity.Error:
                    SystemSounds.Hand.Play();
                    break;
                default:
                    break;
            }
        }

        private void ClickPrimaryInfoBar(InfoBar sender, object args)
        {
            SaveBarBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
        }

        private void ClickGenerateHieararchy(object sender, RoutedEventArgs e)
        {
            var subject1Path = Path.Combine(WorkingPath, "Subject1");
            var subject2Path = Path.Combine(WorkingPath, "Subject2");
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim2"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim3"));

            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim2"));

            SetInfoBar(InfoBarSeverity.Success, "Example generated", "Rename your folders, create new ones, or remove them accordingly, then put your frames inside the aniamtion folders");
       
            ReloadTreeViewAndConfigs();
        }



 

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            List<string> fullSelectionPath = [];
            fullSelectionPath.AddRange(ProgramConfig.SelectedNodePath!);
            fullSelectionPath.Add(ProgramConfig.SelectedNodes!.Last());
            string subjectName = fullSelectionPath[0];

            SetInfoBar(InfoBarSeverity.Informational, "Generating", $"{subjectName} is being generated", false);
            IsGenerating = true;
            
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await Task.Run(async () => await Processer.StartProcessAsync(subjectName));
                stopwatch.Stop();
                if (string.IsNullOrWhiteSpace(ProgramConfig.AssetConfig!.InterfaceConfig.GeneratePath))
                {
                    SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"You can find the spritesheet in _generated ({stopwatch.ElapsedMilliseconds}ms)");
                }
                else
                {
                    SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"You can find the spritesheet in {ProgramConfig.AssetConfig!.InterfaceConfig.GeneratePath} ({stopwatch.ElapsedMilliseconds}ms)");
                }  
            }
            catch (Exception er)
            {
                stopwatch.Stop();
                SetInfoBar(InfoBarSeverity.Error, "Generation failed", er.Message);
            }

            IsGenerating = false;
        }

        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                WorkingPathTextBox.Text = folder.Path;
            }
        }

        private async void BrowseGenerateFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                GeneratePathTextBox.Text = folder.Path;
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures"));
        }

        private async void TreeViewControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(TreeViewControl).Properties.IsRightButtonPressed)
                return;

            var originalSource = e.OriginalSource as DependencyObject;
            while (originalSource != null && originalSource is not TreeViewItem)
                originalSource = VisualTreeHelper.GetParent(originalSource);

            if (originalSource is not TreeViewItem item)
                return;
                
            var node = TreeViewControl.NodeFromContainer(item);

            if (node == null)
                return;
            
            string? configPath = null;
            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            switch (depth)
            {
                case ItemDepth.Subject:
                    configPath = Path.Combine(WorkingPath, ((node.Content as TreeItem)!).Text);
                    break;
                case ItemDepth.Animation:
                    configPath = Path.Combine(WorkingPath, ((node.Parent.Content as TreeItem)!).Text, ((node.Content as TreeItem)!).Text);
                    break;
                case ItemDepth.Frame:
                    var subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
                    var animationName = (node.Parent.Content as TreeItem)!.Text;
                    var frameIndex = int.Parse((node.Content as TreeItem)!.Text);
                    configPath = Path.Combine(WorkingPath, subjectName, animationName, ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[frameIndex].Name + ".png");
                    break;
            }

            if (configPath == null)
                return;
            
            if (Directory.Exists(configPath))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("file:///" + configPath.Replace('\\', '/')));
            }     
            else if(File.Exists(configPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{configPath}\"",
                    UseShellExecute = true
                });
            }        
        }

        private void BottomBarStackPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if ((HelpPanel.Children[0] as ScrollViewer) == null) return;
            var HelpStackPanel = ((HelpPanel.Children[0] as ScrollViewer)!.Content as StackPanel)!;
            HelpStackPanel.Padding = new Thickness(
                HelpStackPanel.Padding.Left,
                HelpStackPanel.Padding.Top,
                HelpStackPanel.Padding.Right,
                PrimaryInfoBar.ActualHeight + 12);

            var SubjectStackPanel = ((SubjectPanel.Children[0] as ScrollViewer)!.Content as StackPanel)!;
            SubjectStackPanel.Padding = new Thickness(
                SubjectStackPanel.Padding.Left,
                SubjectStackPanel.Padding.Top,
                SubjectStackPanel.Padding.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);

            var AnimationStackPanel = ((AnimationsPanel.Children[0] as ScrollViewer)!.Content as StackPanel)!;
            AnimationStackPanel.Padding = new Thickness(
                AnimationStackPanel.Padding.Left,
                AnimationStackPanel.Padding.Top,
                AnimationStackPanel.Padding.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);

            CanvasBorder.Margin = new Thickness(
                CanvasBorder.Margin.Left,
                CanvasBorder.Margin.Top,
                CanvasBorder.Margin.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);

            FrameConfigPanel.Padding = new Thickness(
                FrameConfigPanel.Padding.Left,
                FrameConfigPanel.Padding.Top,
                FrameConfigPanel.Padding.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);
            }

        private async void ProgramDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var exeDir = AppContext.BaseDirectory;
            await Windows.System.Launcher.LaunchUriAsync(new Uri("file:///" + exeDir.Replace('\\', '/')));
        }

        private void ClearKeyboardState()
        {
            _isCtrlHeld = false;
            FrameCoordinateEditorControl.ClearNudgeKeyState();
        }

        private void SyncKeyboardState()
        {
            _isCtrlHeld = IsVirtualKeyDown(Windows.System.VirtualKey.Control) ||
                          IsVirtualKeyDown(Windows.System.VirtualKey.LeftControl) ||
                          IsVirtualKeyDown(Windows.System.VirtualKey.RightControl);

            bool isFrameEditorOpen =
                FramePanel.Visibility == Visibility.Visible &&
                TreeViewControl.SelectedNode != null &&
                (TreeViewControl.SelectedNode.Content as TreeItem)!.Depth == ItemDepth.Frame &&
                FrameCoordinateEditorControl.PreviewSpriteFrames.Count > 0;

            if (isFrameEditorOpen)
            {
                FrameCoordinateEditorControl.SyncNudgeKeyState(IsVirtualKeyDown);
            }
            else
            {
                FrameCoordinateEditorControl.ClearNudgeKeyState();
            }
        }

        private static bool IsVirtualKeyDown(Windows.System.VirtualKey key)
            => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        private void MainRootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.R && FramePanel.Visibility == Visibility.Visible)
            {
                FrameCoordinateEditorControl.ToggleShowPreviousFrame();
                e.Handled = true;
                return;
            }

            if (HandleTreeViewHotkeys(e))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Control ||
                e.Key == Windows.System.VirtualKey.LeftControl ||
                e.Key == Windows.System.VirtualKey.RightControl)
            {
                _isCtrlHeld = true;
                return;
            }

            bool isFrameEditorOpen =
                FramePanel.Visibility == Visibility.Visible &&
                TreeViewControl.SelectedNode != null &&
                (TreeViewControl.SelectedNode.Content as TreeItem)!.Depth == ItemDepth.Frame &&
                FrameCoordinateEditorControl.PreviewSpriteFrames.Count > 0;

            if (isFrameEditorOpen && FrameCoordinateEditorControl.HandleNudgeKeyDown(e.Key))
            {
                e.Handled = true;
            }
        }

        private void MainRootGrid_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Control ||
                e.Key == Windows.System.VirtualKey.LeftControl ||
                e.Key == Windows.System.VirtualKey.RightControl)
            {
                _isCtrlHeld = false;
            }

            FrameCoordinateEditorControl.HandleNudgeKeyUp(e.Key);
        }

        private bool HandleTreeViewHotkeys(KeyRoutedEventArgs e)
        {
            TreeViewNode? selectedNode = TreeViewControl.SelectedNode;
            if (selectedNode == null)
            {
                return false;
            }

            bool ctrlHeld = e.KeyStatus.IsMenuKeyDown || IsCtrlHeld ||
                            e.Key == Windows.System.VirtualKey.Control ||
                            e.Key == Windows.System.VirtualKey.LeftControl ||
                            e.Key == Windows.System.VirtualKey.RightControl;

            if (ctrlHeld && e.Key == Windows.System.VirtualKey.A)
            {
                IList<TreeViewNode> siblings = selectedNode.Parent?.Children ?? TreeViewControl.RootNodes;
                ProgramConfig.SelectedNodes = [];
     
                ItemDepth depth = (selectedNode.Content as TreeItem)!.Depth;
                switch (depth)
                {
                    case ItemDepth.Subject:
                        _currentConfigs = [.. ProgramConfig.AssetConfig!.SubjectConfigs!.Values];
                        break;
                    case ItemDepth.Animation:
                        var subjectName = (selectedNode.Parent!.Content as TreeItem)!.Text;
                        _currentConfigs = [.. ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs!.Values];
                        break;
                    case ItemDepth.Frame:
                        subjectName = (selectedNode.Parent!.Parent.Content as TreeItem)!.Text;
                        string animationName = (selectedNode.Parent.Content as TreeItem)!.Text;
                        _currentConfigs = [.. ProgramConfig.AssetConfig!.SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs!];
                        break;                  
                    default:
                        _currentConfigs = [];
                        break;
                }

                foreach (TreeViewNode sibling in siblings)
                {
                    var node = (sibling.Content as TreeItem)!;
                    ProgramConfig.SelectedNodes.Add(node.Text);
                    node.IsSelected = true;
                }
    
                ChangeConfigPanelAsync(siblings.First(), false);
                TreeViewControl.SelectedNode = siblings.First();
                
                return true;
            }

            if (e.Key != Windows.System.VirtualKey.Q && e.Key != Windows.System.VirtualKey.E)
            {
                return false;
            }

            IList<TreeViewNode> siblingList = selectedNode.Parent?.Children ?? TreeViewControl.RootNodes;
            int currentIndex = -1;
            for (int i = 0; i < siblingList.Count; i++)
            {
                if (siblingList[i] == selectedNode)
                {
                    currentIndex = i;
                    break;
                }
            }

            int newIndex = e.Key == Windows.System.VirtualKey.Q ? currentIndex - 1 : currentIndex + 1;
            if (newIndex < 0)
            {
                newIndex = siblingList.Count - 1;
            }
            else if (newIndex >= siblingList.Count)
            {
                newIndex = 0;
            }

            TreeViewControl.SelectedNode = siblingList[newIndex];
            ChangeConfigPanelAsync(siblingList[newIndex], false);
     
            return true;
        }

        private void ClearAllTreeItemSelections()
        {
            foreach (TreeViewNode node in TreeViewControl.RootNodes)
            {
                ClearNodeSelection(node);
            }
        }

        private static void ClearNodeSelection(TreeViewNode node)
        {
            (node.Content as TreeItem)!.IsSelected = false;
            foreach (TreeViewNode child in node.Children)
            {
                ClearNodeSelection(child);
            }
        }

        void CheckForUpdateIfNeeded()
        {
            var now = DateTime.UtcNow;

            if (ProgramConfig.LastUpdateCheck.HasValue)
            {
                var lastCheck = ProgramConfig.LastUpdateCheck.Value;

                if (lastCheck.Date == now.Date)
                    return;
            }

            ProgramConfig.LastUpdateCheck = now;

            CheckForUpdateAsync();
        }

        async void CheckForUpdateAsync()
        {
            string current = GetCurrentVersion();

            string? latest = await UpdateChecker.GetLatestVersionAsync();

            if (latest != null && IsNewer(latest, current))
            {
                UpdateBadge.Visibility = Visibility.Visible;
                UpdateInfoBar.IsOpen = true;
            }
        }

        public static string GetCurrentVersion()
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public static bool IsNewer(string latest, string current)
        {
            latest = latest.TrimStart('v');

            Version latestVersion = new(latest);
            Version currentVersion = new(current);

            return latestVersion > currentVersion;
        }

        private void PopulateAlsoKnownAsList()
        {
            var animConf = GetCurrentAnimationConfig();
            AlsoKnownAsEntries.Clear();
            var defaultRange = (animConf.InterfaceConfig as AnimationInterfaceConfig)?.Range ?? new FramesToMMSpriteResources.DataConfig.RangeConfig();
            if(animConf.AlsoKnownAs != null)
            {
                foreach (var kv in animConf.AlsoKnownAs.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var range = kv.Value ?? new FramesToMMSpriteResources.DataConfig.RangeConfig(defaultRange.From, defaultRange.To);
                    AlsoKnownAsEntries.Add(new AlsoKnownAsEntry(kv.Key, range));
                }
            }
        }

        private void AlsoKnownAsAddButton_Click(object sender, RoutedEventArgs e)
        {
            string newItem = AlsoKnownAsTextBox.Text.Trim();
            if (string.IsNullOrEmpty(newItem)) return;

            var animConf = GetCurrentAnimationConfig();
            if (animConf.AlsoKnownAs == null)
            {
                animConf.AlsoKnownAs = new Dictionary<string, FramesToMMSpriteResources.DataConfig.RangeConfig>();
            }

            animConf.AlsoKnownAs[newItem] = new();

            int insertIndex = 0;
            for (int i = 0; i < AlsoKnownAsEntries.Count; i++)
            {
                if (string.Compare(newItem, AlsoKnownAsEntries[i].Name, StringComparison.Ordinal) < 0)
                {
                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }

            AlsoKnownAsEntries.Insert(insertIndex, new AlsoKnownAsEntry(newItem, new()));
            AlsoKnownAsTextBox.Text = "";
        }

        private void AlsoKnownAsRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is AlsoKnownAsEntry entry)
            {
                var animConf = GetCurrentAnimationConfig();
                if (animConf.AlsoKnownAs != null)
                {
                    animConf.AlsoKnownAs.Remove(entry.Name);
                }
                AlsoKnownAsEntries.Remove(entry);
            }
        }

        private void AlsoKnownAsEditTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var editBox = sender as TextBox;
            if (editBox == null) return;
 
            editBox.Tag = (editBox.DataContext as AlsoKnownAsEntry)?.Name ?? editBox.Text;
        }

        private void AlsoKnownAsEditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var editBox = sender as TextBox;
            if (editBox == null) return;

            var entry = editBox.DataContext as AlsoKnownAsEntry;
            if (entry == null) return;

            string oldValue = editBox.Tag as string ?? entry.Name;
            string newValue = editBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(newValue))
            {
                editBox.Text = oldValue;
                return;
            }

            if (newValue == oldValue)
            {
                return;
            }

            var animConf = GetCurrentAnimationConfig();
            if (animConf.AlsoKnownAs == null) animConf.AlsoKnownAs = new Dictionary<string, FramesToMMSpriteResources.DataConfig.RangeConfig>();

            if (animConf.AlsoKnownAs.ContainsKey(newValue))
            {
                editBox.Text = oldValue;
                return;
            }

            var range = animConf.AlsoKnownAs.ContainsKey(oldValue) ? animConf.AlsoKnownAs[oldValue] : entry.Range;
            animConf.AlsoKnownAs.Remove(oldValue);
            animConf.AlsoKnownAs[newValue] = range;

            int index = AlsoKnownAsEntries.IndexOf(entry);
            if (index >= 0)
            {
                AlsoKnownAsEntries.RemoveAt(index);

                int insertIndex = 0;
                for (int i = 0; i < AlsoKnownAsEntries.Count; i++)
                {
                    if (string.Compare(newValue, AlsoKnownAsEntries[i].Name, StringComparison.Ordinal) < 0)
                    {
                        insertIndex = i;
                        break;
                    }
                    insertIndex = i + 1;
                }

                var newEntry = new AlsoKnownAsEntry(newValue, range);
                AlsoKnownAsEntries.Insert(insertIndex, newEntry);
            }
        }

        private void AlsoKnownAsEditTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                var editBox = sender as TextBox;
                if (editBox != null)
                {
                    var button = (editBox.Parent as Grid)?.Children.OfType<Button>().FirstOrDefault();
                    if (button != null)
                    {
                        button.Focus(FocusState.Programmatic);
                    }
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                var editBox = sender as TextBox;
                if (editBox != null)
                {
                    editBox.Text = editBox.Tag as string ?? (editBox.DataContext as AlsoKnownAsEntry)?.Name ?? "";
                }
            }
        }

        private async void ProgramSaveDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            
            await Windows.System.Launcher.LaunchUriAsync(new Uri("file:///" + GetUserConfigDirectory().Replace('\\', '/')));
        }


    }
}
