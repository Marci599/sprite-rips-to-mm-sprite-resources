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
using System.IO;
using System.Linq;
using System.Media;
using System.Numerics;
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
namespace FramesToMMSpriteResources
{
    public enum ItemDepth
    {
        GameTheme = 0,
        Subject = 1,
        Animation = 2,
        Frame = 3
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
            CountText = oldCount + " → " + newCount;
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

    public class SpriteFrames
    {
   
        public List<SpriteFrame> LoadedSpriteFrames = [];
        public IntVector2 Size;

        public SpriteFrames() { }
        public SpriteFrames(List<SpriteFrame> loadedSpriteFrames, IntVector2 size)
        {
            LoadedSpriteFrames = loadedSpriteFrames;
            Size = size;
        }
    }

    public class SpriteFrame
    {
        public SKBitmap WriteableBitmap;
        public SKRectI CroppedRect;

        public SpriteFrame(SKBitmap writeableBitmap, SKRectI croppedRect)
        {
            WriteableBitmap = writeableBitmap;
            CroppedRect = croppedRect;
  
        }
    }

    public sealed partial class MainWindow : Window
    {
        private static readonly string CONFIG_FILENAME = "config.json";

        public static string WorkingPath = AppContext.BaseDirectory;

        public static ProgramConfig ProgramConfig;

        public static HashSet<object> _currentConfigs;



        bool _isActivated = false;
        bool _isWindowActive = false;

        public static bool IsUsingGameThemes = false;

        bool _isHierarchyError = true;

        private readonly int _fadeOutMs = 50;
        private readonly int _fadeInMs = 100;

        private static bool _isCtrlHeld = false;
        public static bool IsCtrlHeld => _isCtrlHeld;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private bool _isGeneratePanelShowed = true;

        public string[] AnimationSpriteFramePath = new string[3];

        public ObservableCollection<string> BreadcrumbItems { get; } = new();

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
                FramesLoadingBorder.Child.Visibility = Visibility.Collapsed;
            }
            else
            {
                FramesLoadingBorder.Child.Visibility = Visibility.Visible;
            }

            if (!_isGenerating && !_isLoadingFrames && _isWindowActive)
            {
                FramesLoadingBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                FramesLoadingBorder.Visibility = Visibility.Visible;
            }
            
        }

        void CheckForAllowGenerating()
        {
            bool isEnabled = (_isWindowActive && !_isGenerating && _isEnoughFrames);

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
            bool isEnabled = (_isWindowActive && !_isGenerating);

            ControlEnabler.IsEnabled = isEnabled;
            HeaderBreadcrumbBar.IsEnabled = isEnabled;
            TreeViewControl.IsEnabled = isEnabled;
            SettingsToggleButton.IsEnabled = isEnabled;
            CheckForAllowGenerating();
            CheckForAllowFrameEditing();
        }


        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TreeItem))]
        public MainWindow()
        {
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 625));
            AppWindow.SetIcon("Assets/icon.ico");

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 735;
            presenter.PreferredMinimumHeight = 500;

            AppWindow.SetPresenter(presenter);

       

            ProgramConfig = LoadProgramConfig();

            SetUpTreeViewAndConfigs();

            Activated -= MainWindow_Activated;
            Activated += MainWindow_Activated;

            HeaderBreadcrumbBar.ItemsSource = BreadcrumbItems;

            HeaderBreadcrumbBar.ItemClicked -= BreadcrumbBar_ItemClicked;
            HeaderBreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;

            ProgramNameTextBlock.Text += GetCurrentVersion(); 
        
            CheckForUpdateIfNeeded();

            AppWindow.Closing -= AppWindow_Closing;
            AppWindow.Closing += AppWindow_Closing;

            if (Content is UIElement root)
            {
                root.AddHandler(UIElement.PointerPressedEvent,
                    new PointerEventHandler(MainWindow_PointerPressed),
                    handledEventsToo: true);
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


        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {       
            args.Cancel = true;
   
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                MainRootGrid.Focus(FocusState.Programmatic);
                SetInfoBar(InfoBarSeverity.Informational, "Saving", "The program will close soon");
                await Task.Delay(30);
                SaveAllConfigs();
                this.Close();

            });
        }

        bool _ableToRelaod = true;
        bool _waitingForSecondaryActivation = false;

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            MainRootGrid.Focus(FocusState.Programmatic);
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
           
                    _isWindowActive = false;
                    ClearKeyboardState();
                    CheckForAllowProgramEditing();
                    cts?.Cancel();
                    ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
                    WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
                    FrameCoordinateEditorControl.UnloadAnimation();

                    await Task.Delay(30);
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

        async void ActivateProgram()
        {
            _waitingForSecondaryActivation = false;
 

            if (_isActivated)
            {
                ProgramConfig = LoadProgramConfig();
                ReloadTreeViewAndConfigs();
            }

            _isActivated = true;

            ReduceFileSizeCheckBox.IsChecked = ProgramConfig.ReduceFileSize;
            WorkingPathTextBox.Text = ProgramConfig.WorkingPath;

            ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
            ReduceFileSizeCheckBox.Click += ReduceFileSizeCheckBox_Click;

            WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
            WorkingPathTextBox.TextChanged += WorkingPathTextBox_LostFocus;
   
            _isWindowActive = true;

            await Task.Delay(1);

            CheckForAllowProgramEditing();
            SyncKeyboardState();
        }

        private async void WorkingPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAllConfigs();
            ProgramConfig.WorkingPath = (sender as TextBox)!.Text;
            FrameCoordinateEditorControl.UnloadAnimation();
            ReloadTreeViewAndConfigs();
        }

        private void ReduceFileSizeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            ProgramConfig.ReduceFileSize = (sender as CheckBox)!.IsChecked!.Value;
        }

        void SaveAllConfigs()
        {
            if (_isHierarchyError)
            {
                SaveProgramConfig();
                return;
            }

            if (IsUsingGameThemes)
            {
                SaveProgramConfig();
                var gameThemeDirs = Directory.GetDirectories(WorkingPath);

                foreach (var gameThemeDir in gameThemeDirs)
                {
                    string gameThemeName = Path.GetFileName(gameThemeDir);
                    var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);

                    SaveJson(gameThemeConfigPath, ProgramConfig.GameThemeConfigs![gameThemeName]);
                    SaveSubjects(gameThemeDir, gameThemeName);
                }
            }
            else
            {
                ProgramConfig.IsHd = ProgramConfig.GameThemeConfigs!["Game Theme"].IsHd;
                SaveProgramConfig();
                SaveSubjects(WorkingPath, "Game Theme");
            }
        }

        void SaveSubjects(string gameThemeDir, string gameThemeName)
        {
            var subjectDirs = Directory.GetDirectories(gameThemeDir);
            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);
                var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);

                SaveJson(subjectConfigPath, ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName]);

                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                foreach (var animationDir in animationDirs)
                {
                    string animationName = Path.GetFileName(animationDir);
                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);

                    SaveJson(animationConfigPath, ProgramConfig.GameThemeConfigs[gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName]);
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

        private ProgramConfig LoadProgramConfig()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME);
            return LoadJson<ProgramConfig>(configPath);
        }

        void SaveProgramConfig()
        {
            SaveJson(Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME), ProgramConfig);
        }

        TreeItem GetSelectedTreeItem()
        {
            return (TreeViewControl.SelectedNode.Content as TreeItem)!;
        }

        void ReloadTreeViewAndConfigs()
        {
            TreeViewControl.RootNodes.Clear();
            ProgramConfig.GameThemeConfigs = [];
            TryCloseInfoBar();
            SetUpTreeViewAndConfigs();
        }

        TreeViewNode? lastSelectedNode = null;
        void SetUpTreeViewAndConfigs()
        {
            lastSelectedNode = null;

            _isHierarchyError = false;
                              
            TreeViewControl.ItemInvoked -= TreeViewControl_ItemInvoked;
            TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;

            TreeViewControl.PointerPressed -= TreeViewControl_PointerPressed;
            TreeViewControl.PointerPressed += TreeViewControl_PointerPressed;

            TreeViewControl.Expanding -= TreeViewControl_Expanding;
            TreeViewControl.Expanding += TreeViewControl_Expanding;

            TreeViewControl.Collapsed -= TreeViewControl_Collapsed;
            TreeViewControl.Collapsed += TreeViewControl_Collapsed;

            WorkingPath = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(ProgramConfig.WorkingPath))
            {
                WorkingPath = ProgramConfig.WorkingPath;
            }

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

            IsUsingGameThemes = true;
  
            var firstLevelDirs = Directory.GetDirectories(WorkingPath);

            foreach (var first in firstLevelDirs)
            {
                if (!AreSubjectsCorrect(first))
                {
                    IsUsingGameThemes = false;
                    break;
                }
            }

            if (IsUsingGameThemes)
            {
                ProgramConfig.SelectedNodePath ??= [];
                var gameThemeDirs = Directory.GetDirectories(WorkingPath);

                foreach (var gameThemeDir in gameThemeDirs)
                {
                    string gameThemeName = Path.GetFileName(gameThemeDir);

                    var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                    GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath);

                    SetUpSubjectTreeViewAndConfigs(gameThemeDir, gameThemeName, gameThemeConfig);
                }

                ChangeEditorPanel();
            }
            else
            {
                if (AreSubjectsCorrect(WorkingPath))
                {
                    ProgramConfig.SelectedNodePath ??= [];
                    GameThemeConfig gameThemeConfig = new(ProgramConfig.IsHd, true);

                    SetUpSubjectTreeViewAndConfigs(WorkingPath, "Game Theme", gameThemeConfig);

                    ChangeEditorPanel();
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
                        SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong... Do you have the \"raw\" folders inside the subject folders?", false);
                    }
                }
            }
            if (lastSelectedNode != null)
                TreeViewControl.SelectedNode = lastSelectedNode;          
        }

        void ChangeEditorPanel()
        {
            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;

            if (lastSelectedNode == null)
            {
                OpenSettingsAsync();
            }
            else
            {
                ChangeConfigPanelIfNecessary(lastSelectedNode, false, true);
            }
        }

        void SetUpSubjectTreeViewAndConfigs(string gameThemeDir, string gameThemeName, GameThemeConfig gameThemeConfig)
        {
            var gameThemeTreeItem = new TreeViewNode { Content = new TreeItem(gameThemeName, ItemDepth.GameTheme), IsExpanded = gameThemeConfig.IsExpanded };

            var subjectDirs = Directory.GetDirectories(gameThemeDir);         

            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);

                var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);
                SubjectConfig subjectConfig = LoadJson<SubjectConfig>(subjectConfigPath);

                var subjectTreeItem = new TreeViewNode { Content = new TreeItem(subjectName, ItemDepth.Subject), IsExpanded = subjectConfig.IsExpanded };

                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                int framesSum = 0;
                foreach (var animationDir in animationDirs)
                {
                    string animationName = Path.GetFileName(animationDir);

                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);
                    AnimationConfig animationConfig = LoadJson<AnimationConfig>(animationConfigPath);         

                    var frameFiles = Directory.GetFiles(animationDir);
                    var fileCount = frameFiles.Length;
                    
                    if (File.Exists(animationConfigPath))
                    {
                        fileCount--;
                    }

                    framesSum += fileCount;
                    TreeItem treeItem;

                    if(animationConfig.GeneratedFrameCount == -1)
                    {
                        animationConfig.GeneratedFrameCount = fileCount;
                    }

                    if (animationConfig.GeneratedFrameCount == fileCount)
                    {
                        treeItem = new(animationName, ItemDepth.Animation, fileCount);
                    }
                    else
                    {
                        treeItem = new(animationName, ItemDepth.Animation, animationConfig.GeneratedFrameCount, fileCount);
                    }
                    
                    var animationTreeItem = new TreeViewNode { Content = treeItem, IsExpanded = animationConfig.IsExpanded};
                    
                    int frameIndex = 0;

                    animationConfig.FrameCongfigs ??= [];

                    foreach (var frameFile in frameFiles)
                    {
                        if(Path.GetExtension(frameFile) != ".json")
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

                            string frameName = frameIndex.ToString("D3");
                            TreeItem frameTreeItem = new(frameName, ItemDepth.Frame);
                            var frameTreeViewNode = new TreeViewNode { Content = frameTreeItem };

                            animationTreeItem.Children.Add(frameTreeViewNode);

                            if (ProgramConfig.SelectedNodes != null &&
                                ProgramConfig.SelectedNodePath!.Count == 3 &&
                                ProgramConfig.SelectedNodePath[0] == gameThemeName &&
                                ProgramConfig.SelectedNodePath[1] == subjectName &&
                                ProgramConfig.SelectedNodePath[2] == animationName &&
                                ProgramConfig.SelectedNodes.Contains(frameName))
                            {

                                TreeViewControl.SelectedNode = frameTreeViewNode;
                                GetSelectedTreeItem().IsSelected = true;
                                TreeViewControl.SelectedNode = null;

                                if (ProgramConfig.SelectedNodes.Last() == frameName)
                                {
                                    lastSelectedNode = frameTreeViewNode;
                                }
                            }
                            frameIndex++;
                        }
                    }

                    subjectConfig.AnimationConfigs![animationName] = animationConfig;
                    subjectTreeItem.Children.Add(animationTreeItem);

                    if (ProgramConfig.SelectedNodes != null &&
                        ProgramConfig.SelectedNodePath!.Count == 2 &&
                        ProgramConfig.SelectedNodePath[0] == gameThemeName &&
                        ProgramConfig.SelectedNodePath[1] == subjectName &&
                        ProgramConfig.SelectedNodes.Contains(animationName))
                    {

                        TreeViewControl.SelectedNode = animationTreeItem;
                        GetSelectedTreeItem().IsSelected = true;
                        TreeViewControl.SelectedNode = null;

                        if (ProgramConfig.SelectedNodes.Last() == animationName)
                        {
                            lastSelectedNode = animationTreeItem;
                        }
                    }
                }

                (subjectTreeItem.Content as TreeItem)!.Count = framesSum;
                (subjectTreeItem.Content as TreeItem)!.CountText = framesSum.ToString();

                gameThemeConfig.SubjectConfigs![subjectName] = subjectConfig;

                gameThemeTreeItem.Children.Add(subjectTreeItem);

                if (ProgramConfig.SelectedNodePath != null &&
                ProgramConfig.SelectedNodes != null &&
                ProgramConfig.SelectedNodePath.Count == 1 &&
                ProgramConfig.SelectedNodePath[0] == gameThemeName &&
                ProgramConfig.SelectedNodes.Contains(subjectName))
                {
                    TreeViewControl.SelectedNode = subjectTreeItem;
                    GetSelectedTreeItem().IsSelected = true;
                    TreeViewControl.SelectedNode = null;

                    if (ProgramConfig.SelectedNodes.Last() == subjectName)
                    {
                        lastSelectedNode = subjectTreeItem;
                    }
                }
            }

            ProgramConfig.GameThemeConfigs![gameThemeName] = gameThemeConfig;

            TreeViewControl.RootNodes.Add(gameThemeTreeItem);

            if (ProgramConfig.SelectedNodes != null &&
                (ProgramConfig.SelectedNodePath!.Count == 0) &&
                ProgramConfig.SelectedNodes.Contains(gameThemeName))
            {
                TreeViewControl.SelectedNode = gameThemeTreeItem;
                GetSelectedTreeItem().IsSelected = true;
                TreeViewControl.SelectedNode = null;

                if (ProgramConfig.SelectedNodes.Last() == gameThemeName)
                {
                    lastSelectedNode = gameThemeTreeItem;
                }
            }         
        }

        void UpdateBreadcrumb(params string[] items)
        {
            BreadcrumbItems.Clear();
            foreach (var item in items)
                BreadcrumbItems.Add(item);
        }

        private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            int clickedIndex = args.Index;

            while (ProgramConfig.SelectedNodePath!.Count > clickedIndex + 1)
            {
                ProgramConfig.SelectedNodePath.RemoveAt(ProgramConfig.SelectedNodePath.Count - 1);
            }

            TreeViewNode? selectedNode = null;
            foreach (TreeViewNode gameThemeNode in TreeViewControl.RootNodes)
            {
                if ((gameThemeNode.Content as TreeItem)!.Text == ProgramConfig.SelectedNodePath[0])
                {
                    if (ProgramConfig.SelectedNodePath.Count == 1)
                    {
                        TreeViewControl.SelectedNode = gameThemeNode;
                        selectedNode = gameThemeNode;
                        break;
                    }
                    else
                    {
                        foreach (TreeViewNode subjectNode in gameThemeNode.Children)
                        {
                            if ((subjectNode.Content as TreeItem)!.Text == ProgramConfig.SelectedNodePath[1])
                            {
                                if (ProgramConfig.SelectedNodePath.Count == 2)
                                {
                                    TreeViewControl.SelectedNode = subjectNode;
                                    selectedNode = subjectNode;
                                    break;
                                }
                                else
                                {
                                    foreach (TreeViewNode animationNode in subjectNode.Children)
                                    {
                                        if ((animationNode.Content as TreeItem)!.Text == ProgramConfig.SelectedNodePath[2])
                                        {
                                            if (ProgramConfig.SelectedNodePath.Count == 3)
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
                        break;
                    }
                }
            }

            MainRootGrid.Focus(FocusState.Programmatic);

            FadeOutAllPanels(false, true);

            ChangeConfigPanel(selectedNode!, true);
        }

        private async void AnimateGeneratePanel(bool show)
        {
            if (_isGeneratePanelShowed == show) return;
            _isGeneratePanelShowed = show;

            var compositor = ElementCompositionPreview.GetElementVisual(SaveBarBorder).Compositor;
            var saveBarVisual = ElementCompositionPreview.GetElementVisual(SaveBarBorder);
            var bottomPanelVisual = ElementCompositionPreview.GetElementVisual(BottomBarStackPanel);

            var animationDuration = TimeSpan.FromMilliseconds(150);

            if (show)
            {
                PrimaryInfoBar.CornerRadius = new CornerRadius(8, 8, 0, 0);

                var cubicEaseOut = compositor.CreateCubicBezierEasingFunction(
                       new System.Numerics.Vector2(0.215f, 0.61f),
                       new System.Numerics.Vector2(0.355f, 1.0f)
                   );

                // Fade in SaveBarBorder

                var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();

                opacityAnimation.InsertKeyFrame(0f, 0f);
                opacityAnimation.InsertKeyFrame(1f, 1f, cubicEaseOut);

                opacityAnimation.Duration = animationDuration;

                saveBarVisual.StartAnimation("Opacity", opacityAnimation);

                // Slide up BottomBarStackPanel

                var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();

                offsetAnimation.InsertKeyFrame(0f, new Vector3(0, (float)SaveBarBorder.ActualHeight, 0));
                offsetAnimation.InsertKeyFrame(1f, Vector3.Zero, cubicEaseOut);

                offsetAnimation.Duration = animationDuration;

                bottomPanelVisual.StartAnimation("Offset", offsetAnimation);
            }
            else
            {
                // Fade out SaveBarBorder

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

                // Slide down BottomBarStackPanel

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

        private static bool AreSubjectsCorrect(string path)
        {
            try
            {
                var firstLevelDirs = Directory.GetDirectories(path);
                if (firstLevelDirs.Length == 0)
                {
                    return false;
                }

                foreach (var first in firstLevelDirs)
                {
                    var rawPath = Path.Combine(first, "raw");
                    if (!Directory.Exists(rawPath))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void TreeViewControl_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = (args.InvokedItem as TreeViewNode)!;

            var selectedNodesCount = ProgramConfig.SelectedNodes?.Count ?? 0;

            if(selectedNodesCount > 1)    
            {
                if (IsCtrlHeld && node == TreeViewControl.SelectedNode)
                {
                    var nodeTreeItem = (node.Content as TreeItem)!;
                    nodeTreeItem.IsSelected = false;
                    ProgramConfig.SelectedNodes!.RemoveAt(ProgramConfig.SelectedNodes.Count -1);
                    
          
                    foreach (TreeViewNode nodeInParent in node.Parent.Children)
                    {
                        if((nodeInParent.Content as TreeItem)!.Text == ProgramConfig.SelectedNodes.Last())
                        {
                            node = nodeInParent;
                            break;
                        }
                    }
                    WaitThenSelectNodeAsync(node);
                }

                ChangeConfigPanelIfNecessary(node, true);
            }
            else
            {
                if(TreeViewControl.SelectedNode != node)
                {
                    ChangeConfigPanelIfNecessary(node, true);
                }
                else
                {
                    TreeViewControl.SelectedNode = null;
                }
            }

        }

        async void WaitThenSelectNodeAsync(TreeViewNode node)
        {
            await Task.Delay(1);
            TreeViewControl.SelectedNode = node;
        }

        void ChangeConfigPanelIfNecessary(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            //TreeViewControl.Focus(FocusState.Programmatic);
   
            SettingsToggleButton.IsChecked = false;

   
            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            bool sameDepth = false;
            if(ProgramConfig.SelectedNodes != null && (
               depth == ItemDepth.GameTheme && ProgramConfig.SelectedNodePath!.Count == 0 ||
               depth == ItemDepth.Subject && ProgramConfig.SelectedNodePath!.Count == 1 ||
               depth == ItemDepth.Animation && ProgramConfig.SelectedNodePath!.Count == 2 ||
               depth == ItemDepth.Frame && ProgramConfig.SelectedNodePath!.Count == 3))
            {
                sameDepth = true;
                animate = false;
            }

            FadeOutAllPanels(sameDepth, animate);        
            ChangeConfigPanel(node, animate, nowGenerated);
        }

        async void FadeOutAllPanels(bool sameDepth, bool animate = true)
        {
            var panels = new[] { GameThemePanel, SubjectPanel, AnimationsPanel, FramePanel, HelpPanel };

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

            // Set up the transform if not already present
            if (panel.RenderTransform is not TranslateTransform translateTransform)
            {
                translateTransform = new TranslateTransform();
                panel.RenderTransform = translateTransform;
            }

            var storyboard = new Storyboard();

            // Opacity animation
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

            // Translate Y animation
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

        void HandleSelection(TreeItem selectedNode, bool nowGenerated, List<String> newSelectedNodePath)
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

        void ChangeConfigPanel(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {      
            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            switch (depth)
            {
                case ItemDepth.GameTheme:
                    DisplayGameThemeConfigAsync(node, animate, nowGenerated);
                    break;

                case ItemDepth.Subject:
                    DisplaySubjectConfigAsync(node, animate, nowGenerated);
                    break;

                case ItemDepth.Animation:
                    DisplayAnimationCongifAsync(node, animate, nowGenerated);
                    break;

                case ItemDepth.Frame:
                    DisplayFrameCongifAsync(node, animate, nowGenerated);               
                    break;

                default:
                    DetachAllPanelEvents();
                    break;
            }
        }

        async Task ChangePanelGraphic(bool animate, UIElement panelToShow)
        {
            if (animate)
            {
                await Task.Delay(_fadeOutMs);
                DetachAllPanelEvents();
                FadeInPanel(panelToShow);
            }
            else
            {
                await Task.Delay(30);
                DetachAllPanelEvents();
                panelToShow.Visibility = Visibility.Visible;
            }
        }

        async void DisplayGameThemeConfigAsync(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            AnimateGeneratePanel(show: false);
            var gameThemeName = (node.Content as TreeItem)!.Text;
            var selectedNode = (node.Content as TreeItem)!;

            HandleSelection(selectedNode, nowGenerated, []);
            UpdateBreadcrumb(string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
            TryCloseInfoBar();

            await ChangePanelGraphic(animate, GameThemePanel);
   
            _currentConfigs = [];
            foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
            {
                _currentConfigs.Add(ProgramConfig.GameThemeConfigs![selectedNodeName]);
            }

            var gameThemeConfig = ProgramConfig.GameThemeConfigs![gameThemeName];
            IsHdCheckBox.IsChecked = gameThemeConfig.IsHd;
            IsHdCheckBox.Click += ClickIsHdCheckBox;
        }

        async void DisplaySubjectConfigAsync(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            AnimateGeneratePanel(show: true);
            var gameThemeName = (node.Parent.Content as TreeItem)!.Text;
            var subjectName = (node.Content as TreeItem)!.Text;

            var selectedNode = (node.Content as TreeItem)!;

            HandleSelection(selectedNode, nowGenerated, [gameThemeName]);
            UpdateBreadcrumb(gameThemeName, string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
            CheckFrameCountAndDisplayWarning((node.Content as TreeItem)!.Count);

            GenerateButton.Content = $"Generate {selectedNode.Text}";

            await ChangePanelGraphic(animate, SubjectPanel);

            _currentConfigs = [];
            foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
            {
                _currentConfigs.Add(ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![selectedNodeName]);
            }

            var subjectConfig = ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName];
            subjectConfig.Sheet ??= new SheetConfig();
    

            RemoveBackgroundCheckBox.IsChecked = subjectConfig.RemoveBackground;
            CropSpritesCheckBox.IsChecked = subjectConfig.CropSprites;

            ResizeTextBox.Text = subjectConfig.ResizeToPercent.ToString();
            ColorTextBox.Text = subjectConfig.BackgroundColor;
            ThresholdTextBox.Text = subjectConfig.ColorTreshold.ToString();
            SheetWidthTextBox.Text = subjectConfig.Sheet.Width.ToString();
            SheetHeightTextBox.Text = subjectConfig.Sheet.Height.ToString();
      
            ColorTextBox.Text = subjectConfig.BackgroundColor ?? "";
            UpdateColorPreview();

            RemoveBackgroundCheckBox.Click += ClickRemoveBackground;
            CropSpritesCheckBox.Click += ClickCropSpritesCheckBox;

            ResizeTextBox.ValueChanged += ResizeTextBox_ValueChanged;
            ColorTextBox.TextChanged += ColorTextBox_TextChanged;
            ThresholdTextBox.ValueChanged += ThresholdTextBox_ValueChanged;

            SheetWidthTextBox.ValueChanged += SheetWidthTextBox_ValueChanged;
            SheetHeightTextBox.ValueChanged += SheetHeightTextBox_ValueChanged;
        }

        async void DisplayAnimationCongifAsync(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            AnimateGeneratePanel(show: true);
            var gameThemeName = (node.Parent.Parent.Content as TreeItem)!.Text;
            var subjectName = (node.Parent.Content as TreeItem)!.Text;
            string animationName = (node.Content as TreeItem)!.Text;

            var selectedNode = (node.Content as TreeItem);

            HandleSelection(selectedNode!, nowGenerated, [gameThemeName, subjectName]);
            UpdateBreadcrumb(gameThemeName, subjectName, string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));
            CheckFrameCountAndDisplayWarning((node.Parent.Content as TreeItem)!.Count);

            GenerateButton.Content = $"Generate {ProgramConfig.SelectedNodePath![1]}";

            await ChangePanelGraphic(animate, AnimationsPanel);
   
            _currentConfigs = [];
            foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
            {
                _currentConfigs.Add(ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![selectedNodeName]);
            }

            var animationConfig = ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName];
            animationConfig.RecoverCroppedOffset ??= new RecoverCroppedOffset();
            animationConfig.Offset ??= new Vector2(0, 0);

            RegenerateCheckBox.IsChecked = animationConfig.Regenerate;
            RecoverXCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.X;
            RecoverYCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.Y;

            DelayTextBox.Text = animationConfig.Delay.ToString();

            LoopTypeComboBox.SelectedIndex = animationConfig.LoopType;

            AnimationOffsetXTextBox.Text = animationConfig.Offset.Value.X.ToString();
            AnimationOffsetYTextBox.Text = animationConfig.Offset.Value.Y.ToString();

            RegenerateCheckBox.Click += ClickRegenerateCheckBox;
            RecoverXCheckBox.Click += ClickRecoverXCheckBox;
            RecoverYCheckBox.Click += ClickRecoverYCheckBox;

            DelayTextBox.ValueChanged += DelayTextBox_ValueChanged;

            LoopTypeComboBox.SelectionChanged += LoopTypeComboBox_SelectionChanged;

            AnimationOffsetXTextBox.ValueChanged += AnimationOffsetXTextBox_ValueChanged;
            AnimationOffsetYTextBox.ValueChanged += AnimationOffsetYTextBox_ValueChanged;
        }

        CancellationTokenSource? cts= null;

        async void DisplayFrameCongifAsync(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            
            var gameThemeName = (node.Parent.Parent.Parent.Content as TreeItem)!.Text;
            var subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
            var animationName = (node.Parent.Content as TreeItem)!.Text;
            string frameName = (node.Content as TreeItem)!.Text;


            AnimateGeneratePanel(show: true);
            var selectedNode = (node.Content as TreeItem)!;

            HandleSelection(selectedNode, nowGenerated, [gameThemeName, subjectName, animationName]);

            UpdateBreadcrumb(gameThemeName, subjectName, animationName, string.Join(", ", ProgramConfig.SelectedNodes!.OrderBy(s => s)));

            CheckFrameCountAndDisplayWarning((node.Parent.Parent.Content as TreeItem)!.Count);

            GenerateButton.Content = $"Generate {ProgramConfig.SelectedNodePath![1]}";
            bool isFromFramePanel = (TreeViewControl.SelectedNode == null || (TreeViewControl.SelectedNode.Content as TreeItem)!.Depth == ItemDepth.Frame);
            await ChangePanelGraphic(animate, FramePanel);
            string[] newPath = [gameThemeName, subjectName, animationName];

            bool subjectEquals = (AnimationSpriteFramePath[0] == newPath[0] && AnimationSpriteFramePath[1] == newPath[1]);
            bool animationEquals = (subjectEquals && AnimationSpriteFramePath[2] == newPath[2]);
            if (!animationEquals)
            {
                if (!subjectEquals || (TreeViewControl.SelectedNode == null || !isFromFramePanel && animationName != AnimationSpriteFramePath[2]))
                {
                    FrameCoordinateEditorControl.UnloadAnimation();
                }
                FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames = [];
                AnimationSpriteFramePath = newPath;
                cts?.Cancel();
            }

            _currentConfigs = [];
            foreach (string selectedNodeName in ProgramConfig.SelectedNodes!)
            {
                _currentConfigs.Add(ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[int.Parse(selectedNodeName)]);
            }

            var subjectConfig = ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName];
            var animationConfig = subjectConfig.AnimationConfigs![animationName];
            int selectedIndex = int.Parse(frameName);
            var frameConfig = animationConfig.FrameCongfigs[selectedIndex];

            if (IsLoadingFrames) return;
            cts = new();
            try
            {
                await LoadCoordinateEditorAsync(gameThemeName, subjectName, animationName, subjectConfig, frameName, selectedIndex, frameConfig, node, cts.Token);
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

        async Task LoadCoordinateEditorAsync(string gameThemeName, string subjectName, string animationName, SubjectConfig subjectConfig, string frameName, int selectedIndex, FrameConfig frameConfig, TreeViewNode node, CancellationToken ct)
        {
            string gameThemePath;
            if (IsUsingGameThemes)
            {
                gameThemePath = Path.Combine(WorkingPath, gameThemeName);
            }
            else
            {
                gameThemePath = WorkingPath;
            }

            string animationPath = Path.Combine(gameThemePath, subjectName, "raw", animationName);

            if (FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames.Count == 0)
            {
                IsLoadingFrames = true;

                SpriteFrames tempAnimationSpriteFrames = new([], new(0,0));

                ColorHelper.TryParse(subjectConfig.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                SKColor backgroundSKColor = new(r, g, b, a);

          

                for (int i = 0; i < node.Parent.Children.Count; i++)         
                {
                    FrameConfig frameConfigInLoop = subjectConfig.AnimationConfigs![animationName].FrameCongfigs[i];
                    ct.ThrowIfCancellationRequested();

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

                        tempAnimationSpriteFrames.Size = new(skb.Width, skb.Height);

                        if (backgroundSKColor.Alpha != 0 && subjectConfig.RemoveBackground)
                            ColorHelper.RemoveColorWithThresholdInPlace(skb, backgroundSKColor.Red, backgroundSKColor.Green, backgroundSKColor.Blue, backgroundSKColor.Alpha, subjectConfig.ColorTreshold);

                        var (left, top, right, bottom) = ColorHelper.RectTrimColor(skb, subjectConfig, (backgroundSKColor.Red, backgroundSKColor.Green, backgroundSKColor.Blue, backgroundSKColor.Alpha));
                        SKRectI rect = new(left, top, right, bottom);

                        bool isSame = (left == 0 && top == 0 && right == skb.Width && bottom == skb.Height);
                        if ((subjectConfig.CropSprites || subjectConfig.RemoveBackground || backgroundSKColor.Alpha == 0) && !isSame)
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
                       
                            return new SpriteFrame(cropped, rect);
                        }
                        return new SpriteFrame(skb, rect);

                    }, ct);

                    if (spriteFrame == null)
                        throw new Exception($"Failed to decode image: {framePath}");

                    tempAnimationSpriteFrames.LoadedSpriteFrames.Add(spriteFrame);
                    
                }
                ct.ThrowIfCancellationRequested();

                var selectedNodeAfter = TreeViewControl.SelectedNode;

                if (selectedNodeAfter != null)
                {
                    var selectedNodeAfterContent = selectedNodeAfter.Content as TreeItem;
                    if (selectedNodeAfterContent!.Depth == ItemDepth.Frame)
                    {
                        frameName = selectedNodeAfterContent.Text;
                        selectedIndex = int.Parse(frameName);
                        frameConfig = subjectConfig.AnimationConfigs![animationName].FrameCongfigs[selectedIndex];
                    }
                }

                IsLoadingFrames = false;
                FrameCoordinateEditorControl.LoadAnimation(tempAnimationSpriteFrames, subjectConfig, animationName, backgroundSKColor);
            }

            if (TreeViewControl.SelectedNode != null)
            {
                if (GetSelectedTreeItem().Depth == ItemDepth.Frame)
                {
                    FrameCoordinateEditorControl.SetSpriteIndex(selectedIndex);





                    DirectionNumberBox.Value = subjectConfig.AnimationConfigs![animationName].Direction;
                    SpeedNumberBox.Value = subjectConfig.AnimationConfigs![animationName].Speed;

                    BasedOnRadioButtons.SelectedIndex = (int)subjectConfig.AnimationConfigs![animationName].AlignBasedOn;

                    OffsetXTextBox.Value = frameConfig.Offset.X;
                    OffsetYTextBox.Value = frameConfig.Offset.Y;

                    FrameCoordinateEditorControl.SpritePositionMoved += SpriteOffset_ValueMoved;

                    DirectionNumberBox.ValueChanged += DirectionNumberBox_ValueChanged;
                    SpeedNumberBox.ValueChanged += SpeedNumberBox_ValueChanged;

                    BasedOnRadioButtons.SelectionChanged += BasedOnRadioButtons_SelectionChanged;

                    OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
                    OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;

                    if (_isWindowActive)
                    {
                        SyncKeyboardState();
                    }
                }
            }
        }

        private void DetachAllPanelEvents()
        {
            IsHdCheckBox.Click -= ClickIsHdCheckBox;
            RemoveBackgroundCheckBox.Click -= ClickRemoveBackground;
            CropSpritesCheckBox.Click -= ClickCropSpritesCheckBox;

            ResizeTextBox.ValueChanged -= ResizeTextBox_ValueChanged;
            ColorTextBox.TextChanged -= ColorTextBox_TextChanged;
            ThresholdTextBox.ValueChanged -= ThresholdTextBox_ValueChanged;

            SheetWidthTextBox.ValueChanged -= SheetWidthTextBox_ValueChanged;
            SheetHeightTextBox.ValueChanged -= SheetHeightTextBox_ValueChanged;

            RegenerateCheckBox.Click -= ClickRegenerateCheckBox;
            RecoverXCheckBox.Click -= ClickRecoverXCheckBox;
            RecoverYCheckBox.Click -= ClickRecoverYCheckBox;

            DelayTextBox.ValueChanged -= DelayTextBox_ValueChanged;
            LoopTypeComboBox.SelectionChanged -= LoopTypeComboBox_SelectionChanged;
            AnimationOffsetXTextBox.ValueChanged -= AnimationOffsetXTextBox_ValueChanged;
            AnimationOffsetYTextBox.ValueChanged -= AnimationOffsetYTextBox_ValueChanged;

            FrameCoordinateEditorControl.SpritePositionMoved -= SpriteOffset_ValueMoved;

            DirectionNumberBox.ValueChanged -= DirectionNumberBox_ValueChanged;
            SpeedNumberBox.ValueChanged -= SpeedNumberBox_ValueChanged;

            BasedOnRadioButtons.SelectionChanged -= BasedOnRadioButtons_SelectionChanged;

            OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
            OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;
        }

        FrameConfig GetCurrentFrameConfig()
        {
            var gameThemeName = (TreeViewControl.SelectedNode.Parent.Parent.Parent.Content as TreeItem)!.Text;
            var subjectName = (TreeViewControl.SelectedNode.Parent.Parent.Content as TreeItem)!.Text;
            var animationName = (TreeViewControl.SelectedNode.Parent.Content as TreeItem)!.Text;
            string frameName = (TreeViewControl.SelectedNode.Content as TreeItem)!.Text;
            return ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[int.Parse(frameName)];
        }

        AnimationConfig GetCurrentFrameAnimationConfig()
        {
            var node = TreeViewControl.SelectedNode;
            var gameThemeName = (node.Parent.Parent.Parent.Content as TreeItem)!.Text;
            var subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
            var animationName = (node.Parent.Content as TreeItem)!.Text;

            return ProgramConfig.GameThemeConfigs[gameThemeName].SubjectConfigs[subjectName].AnimationConfigs[animationName];
        }

        public void RefreshOffsetFieldVisually()
        {
            OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
            OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;

            var frameConfig = GetCurrentFrameConfig();

            OffsetXTextBox.Value = frameConfig.Offset.X;
            OffsetYTextBox.Value = frameConfig.Offset.Y;

            OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
            OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;
        }

        private void RemoveMovementButton_Click(object sender, RoutedEventArgs e)
        {


            var animationNode = TreeViewControl.SelectedNode.Parent;
       

            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
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
                        initialPosition -= ConvertToVector2(animationConfig.Direction, animationConfig.Speed);
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


            int rawPositionX = (FrameCoordinateEditorControl.PreviewSpriteFrames.Size.X / 2) * -1;
            int rawPositionY = FrameCoordinateEditorControl.PreviewSpriteFrames.Size.Y;
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();

            if (animationConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                SpriteOffset_ValueChanged(new(rawPositionX, rawPositionY));
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
                        int croppedPositionX = rawPositionX + ((FrameCoordinateEditorControl.PreviewSpriteFrames.Size.X - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Width) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Left;
                        int croppedPositionY = rawPositionY - (FrameCoordinateEditorControl.PreviewSpriteFrames.Size.Y - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Bottom);
                        
                        frameConfigList[i].Offset = new(croppedPositionX, croppedPositionY);

                    }
                }
            }

            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void ALignTopLeftButton_Click(object sender, RoutedEventArgs e)
        {
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            if (animationConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                SpriteOffset_ValueChanged(new(0, 0));
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
                        int croppedPositionX = 0 - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Left;
                        int croppedPositionY = 0 + FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Top;

                        frameConfigList[i].Offset = new(croppedPositionX, croppedPositionY);

                    }
                }
            }
            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }

        private void ALignCenterButton_Click(object sender, RoutedEventArgs e)
        {

            int rawPositionX = (FrameCoordinateEditorControl.PreviewSpriteFrames.Size.X / 2) * -1;
            int rawPositionY = FrameCoordinateEditorControl.PreviewSpriteFrames.Size.Y / 2;
            AnimationConfig animationConfig = GetCurrentFrameAnimationConfig();
            if (animationConfig.AlignBasedOn == AlignBasedOn.RawSpriteSie)
            {
                SpriteOffset_ValueChanged(new(rawPositionX, rawPositionY));
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
                        int croppedPositionX = rawPositionX + ((FrameCoordinateEditorControl.PreviewSpriteFrames.Size.X - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Width) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Left;
                        int croppedPositionY = rawPositionY + (((FrameCoordinateEditorControl.PreviewSpriteFrames.Size.Y - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Height) / 2) - FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames[i].CroppedRect.Top) * -1;

                        frameConfigList[i].Offset = new(croppedPositionX, croppedPositionY);
                    }
                }
            }
            RefreshOffsetFieldVisually();
            FrameCoordinateEditorControl.UpdateVisuals();
        }



        private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {

            SpriteOffset_ValueChanged(new(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, GetCurrentFrameConfig().Offset.Y));
            FrameCoordinateEditorControl.UpdateVisuals();

        }

        private void OffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SpriteOffset_ValueChanged(new IntVector2(GetCurrentFrameConfig().Offset.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value));
            FrameCoordinateEditorControl.UpdateVisuals();

        }

        private void BasedOnRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GetCurrentFrameAnimationConfig().AlignBasedOn = (AlignBasedOn)(sender as RadioButtons)!.SelectedIndex;
        }

        private void DirectionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            GetCurrentFrameAnimationConfig().Direction = double.IsNaN(sender.Value) ? 90 : (float)sender.Value;
        }

        private void SpeedNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            GetCurrentFrameAnimationConfig().Speed = double.IsNaN(sender.Value) ? 0 : (float)sender.Value;
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

        private void AnimationOffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig!.Offset = new Vector2((currentConfig).Offset!.Value.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value);
            }         
        }

        private void AnimationOffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Offset = new Vector2(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, (currentConfig).Offset!.Value.Y);
            }
        }

        private void DelayTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.Delay = double.IsNaN(sender.Value) ? 1 : (int)sender.Value;
            }
        }

        private void LoopTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in _currentConfigs)
            {
                currentConfig.LoopType = (sender as ComboBox).SelectedIndex;
            }
        }

        private void SheetHeightTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Sheet.Height = double.IsNaN(sender.Value) ? null : (int)sender.Value;
            }
        }

        private void SheetWidthTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.Sheet.Width = double.IsNaN(sender.Value) ? null : (int)sender.Value;
            }
        }

        private void ThresholdTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            int threshold = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.ColorTreshold = threshold;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ResizeTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.ResizeToPercent = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
            }
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

        private void ClickCropSpritesCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.CropSprites = (sender as CheckBox)!.IsChecked!.Value;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ClickRemoveBackground(object sender, RoutedEventArgs e)
        {
            bool removeBackground = (sender as CheckBox)!.IsChecked!.Value;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.RemoveBackground = removeBackground;
            }
            AnimationSpriteFramePath = new string[3];
        }

        private void ClickIsHdCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (GameThemeConfig currentConfig in _currentConfigs)
            {
                currentConfig.IsHd = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreview();
            string backgroundColor = (sender as TextBox)!.Text;
            foreach (SubjectConfig currentConfig in _currentConfigs)
            {
                currentConfig.BackgroundColor = backgroundColor;
            }
            AnimationSpriteFramePath = new string[3];
        }


        private void UpdateColorPreview()
        {
            bool valid = TryNormalizeHexToColor(ColorTextBox.Text, out string normalizedHex, out Windows.UI.Color color);
            if (valid)
            {
                ColorPreviewBorder.Background = new SolidColorBrush(color);
                var brush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
                ColorPreviewBorder.BorderBrush = brush;
            }
            else
            {
                ColorPreviewBorder.Background = new SolidColorBrush();
                var brush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                ColorPreviewBorder.BorderBrush = brush;
            }


        }

        private void TreeViewControl_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;

            ItemDepth depth = (node.Content as TreeItem)!.Depth;
            switch (depth)
            {
                case ItemDepth.GameTheme:
                    ProgramConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                case ItemDepth.Subject:
                    ProgramConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                case ItemDepth.Animation:
                    ProgramConfig.GameThemeConfigs![(node.Parent.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
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
                case ItemDepth.GameTheme:
                    ProgramConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;

                case ItemDepth.Subject:
                    ProgramConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;

                case ItemDepth.Animation:
                    ProgramConfig.GameThemeConfigs![(node.Parent.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;

                default:
                    break;
            }
        }

        private void ClickSettings(object sender, RoutedEventArgs e)
        {
            OpenSettingsAsync();
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
            FadeInPanel(HelpPanel);
    
        }

        public void OpenSettingsAndHideGeneratePanelImmediately()
        {
            if (!_isActivated)
            {
                SaveBarBorder.Opacity = 0;

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
            var gameThemePath = Path.Combine(WorkingPath, "GameTheme1");
            var subject1Path = Path.Combine(gameThemePath, "Subject1", "raw");
            var subject2Path = Path.Combine(gameThemePath, "Subject2", "raw");
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim2"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim3"));

            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim2"));

            SetInfoBar(InfoBarSeverity.Success, "Example generated", "Rename your folders, create new ones, or remove them accordingly, then put your frames inside the aniamtion folders");
       
            ReloadTreeViewAndConfigs();
        }

        private static bool TryNormalizeHexToColor(string? input, out string normalizedHex, out Windows.UI.Color color)
        {
            normalizedHex = string.Empty;
            color = new Windows.UI.Color();

            if (string.IsNullOrWhiteSpace(input))
                return true;

            string s = input.Trim();
            if (s.StartsWith('#'))
                s = s[1..];

            s = s.ToUpperInvariant();

            if (!ColorRegex().IsMatch(s))
                return false;

            if (s.Length == 6)
            {
                if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
                {
                    byte r = (byte)((rgb >> 16) & 0xFF);
                    byte g = (byte)((rgb >> 8) & 0xFF);
                    byte b = (byte)(rgb & 0xFF);
                    color = Windows.UI.Color.FromArgb(255, r, g, b);
                    normalizedHex = "#" + s;
                    return true;
                }
            }
            else if (s.Length == 8)
            {
                if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgba))
                {
                    byte r = (byte)((rgba >> 24) & 0xFF);
                    byte g = (byte)((rgba >> 16) & 0xFF);
                    byte b = (byte)((rgba >> 8) & 0xFF);
                    byte a = (byte)(rgba & 0xFF);
                    color = Windows.UI.Color.FromArgb(a, r, g, b);
                    normalizedHex = "#" + s;
                    return true;
                }
            }

            return false;
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\A[0-9A-F]+\z")]
        private static partial System.Text.RegularExpressions.Regex ColorRegex();

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            List<string> fullSelectionPath = [];
            fullSelectionPath.AddRange(ProgramConfig.SelectedNodePath!);
            fullSelectionPath.Add(ProgramConfig.SelectedNodes!.Last());
            string gameThemeName = fullSelectionPath[0];
            string subjectName = fullSelectionPath[1];

            SetInfoBar(InfoBarSeverity.Informational, "Generating", $"{subjectName} is being generated", false);
            IsGenerating = true;
            await Task.Delay(30);

            var stopwatch = Stopwatch.StartNew();
            try
            {
   
                await Task.Run(async () => await Processer.StartProcessAsync(gameThemeName, subjectName));
                stopwatch.Stop();
                SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"You can find the spritesheet in {subjectName}/generated ({stopwatch.ElapsedMilliseconds}ms)");
            }
            catch (Exception er)
            {
                SetInfoBar(InfoBarSeverity.Error, "Generation failed", er.Message);
            }

            SaveAllConfigs();
            FrameCoordinateEditorControl.UnloadAnimation();
            ReloadTreeViewAndConfigs();
    
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
                ProgramConfig.WorkingPath = folder.Path;
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures"));
        }

        private async void TreeViewControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(TreeViewControl).Properties.IsRightButtonPressed)
            {
                var originalSource = e.OriginalSource as DependencyObject;
                while (originalSource != null && originalSource is not TreeViewItem)
                    originalSource = VisualTreeHelper.GetParent(originalSource);

                if (originalSource is TreeViewItem item)
                {
                    var node = TreeViewControl.NodeFromContainer(item);
                    if (node != null)
                    {
                        string? configPath = null;
                        ItemDepth depth = (node.Content as TreeItem)!.Depth;
                        switch (depth)
                        {
                            case ItemDepth.GameTheme:
                                if (!string.IsNullOrWhiteSpace(ProgramConfig.WorkingPath))
                                {
                                    configPath = Path.Combine(WorkingPath, ((node.Content as TreeItem)!).Text);
                                }
                                else
                                {
                                    configPath = WorkingPath;
                                }

                                break;

                            case ItemDepth.Subject:
                                configPath = Path.Combine(WorkingPath, ((node.Parent.Content as TreeItem)!).Text, ((node.Content as TreeItem)!).Text);
                                break;

                            case ItemDepth.Animation:
                                configPath = Path.Combine(WorkingPath, ((node.Parent.Parent.Content as TreeItem)!).Text, ((node.Parent.Content as TreeItem)!).Text, "raw", ((node.Content as TreeItem)!).Text);
                                break;

                            case ItemDepth.Frame:
                                var gameThemeName = (node.Parent.Parent.Parent.Content as TreeItem)!.Text;
                                var subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
                                var animationName = (node.Parent.Content as TreeItem)!.Text;
                                var frameIndex = int.Parse((node.Content as TreeItem)!.Text);
                                configPath = Path.Combine(WorkingPath, gameThemeName, subjectName, "raw", animationName, ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs[frameIndex].Name + ".png");
                                break;
                        }
                        if (configPath != null)
                        {
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
                    }
                }
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

            var GameThemeStackPanel = ((GameThemePanel.Children[0] as ScrollViewer)!.Content as StackPanel)!;
            GameThemeStackPanel.Padding = new Thickness(
                GameThemeStackPanel.Padding.Left,
                GameThemeStackPanel.Padding.Top,
                GameThemeStackPanel.Padding.Right,
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
                FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames.Count > 0;

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
                FrameCoordinateEditorControl.PreviewSpriteFrames.LoadedSpriteFrames.Count > 0;

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

            bool ctrlHeld = e.KeyStatus.IsMenuKeyDown || _isCtrlHeld ||
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
                    case ItemDepth.GameTheme:
                        _currentConfigs = [.. ProgramConfig.GameThemeConfigs!.Values];
                        break;

                    case ItemDepth.Subject:
                        var gameThemeName = (selectedNode.Parent!.Content as TreeItem)!.Text;
                        _currentConfigs = [.. ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs!.Values];
                        break;

                    case ItemDepth.Animation:
                        gameThemeName = (selectedNode.Parent!.Parent.Content as TreeItem)!.Text;
                        var subjectName = (selectedNode.Parent.Content as TreeItem)!.Text;
                        _currentConfigs = [.. ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs!.Values];
                        break;

                    case ItemDepth.Frame:
                        gameThemeName = (selectedNode.Parent!.Parent.Parent.Content as TreeItem)!.Text;
                        subjectName = (selectedNode.Parent.Parent.Content as TreeItem)!.Text;
                        string animationName = (selectedNode.Parent.Content as TreeItem)!.Text;
                        _currentConfigs = [.. ProgramConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].FrameCongfigs!];
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

                
                    ChangeConfigPanel(siblings.First(), false);
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
            ChangeConfigPanel(siblingList[newIndex], false);
     
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
    }
}
