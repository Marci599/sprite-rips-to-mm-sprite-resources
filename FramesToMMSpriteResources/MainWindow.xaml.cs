using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI.Composition;

namespace FramesToMMSpriteResources
{
    public enum ItemDepth
    {
        GameTheme = 0,
        Subject = 1,
        Animation = 2,
        Frame = 3
    }

    public class TreeItem : INotifyPropertyChanged
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

    public struct IntVector2
    {
        public int X { get; }
        public int Y { get; }

        public IntVector2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int SqrMagnitude => X * X + Y * Y;

        public bool Equals(IntVector2 other)
            => X == other.X && Y == other.Y;

        public override bool Equals(object? obj)
            => obj is IntVector2 other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(X, Y);

        public static bool operator ==(IntVector2 left, IntVector2 right)
            => left.Equals(right);

        public static bool operator !=(IntVector2 left, IntVector2 right)
            => !left.Equals(right);

        public override string ToString()
            => $"({X}, {Y})";
    }

    public struct UIntVector2
    {
        public UIntVector2(uint x, uint y) { X = x; Y = y; }

        public uint X { get; }
        public uint Y { get; }

        uint sqrMagnitude
        {
            get { return X * X + Y * Y; }
        }
    }

    public sealed partial class MainWindow : Window
    {

        //TODO: USE ORDERED SET FOR UNSELECTING IN ORDER
        //TODO: FIX COLOR FIELD BEING OVERRIDDEN
        //TODO: WHEN HIERARCHY ERROR, AT FIRST IT DOESN'T SHOW

        private static readonly string CONFIG_FILENAME = "config.json";

        public static string workingPath = AppContext.BaseDirectory;

        public static ProgramConfig programConfig;
        private HashSet<object> currentConfigs;

        bool activated = false;

        public static bool usingGameThemes = false;
        bool hierarchyError = true;

        private bool _isSettingBackgroundColor;
        private string _lastValidBackgroundColor = "";

        MediaPlayer player = new();

        int fadeOutMs = 50;
        int fadeInMs = 100;

        bool _isWindowActive = false;
        public bool IsWindowActive
        {
            get => _isWindowActive;
            set
            {
                _isWindowActive = value;
                CheckForToggleProgramEditing();
            }
        }

        bool _isGenerating = false;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                _isGenerating = value;
                CheckForToggleProgramEditing();
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

        void CheckForAllowGenerating()
        {
            bool isEnabled = (_isWindowActive && !_isGenerating && _isEnoughFrames);
            CanGenerate(isEnabled);
        }

        void CheckForToggleProgramEditing()
        {
            bool isEnabled = (_isWindowActive && !_isGenerating);
            ToggleProgramEditing(isEnabled);
        }

        void ToggleProgramEditing(bool isEnabled)
        {
            ControlEnabler.IsEnabled = isEnabled;
            HeaderBreadcrumbBar.IsEnabled = isEnabled;
            TreeViewControl.IsEnabled = isEnabled;
            SettingsToggleButton.IsEnabled = isEnabled;
            CheckForAllowGenerating();
        }

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
        // Tracks whether the CTRL key is currently held
        private bool _isCtrlHeld = false;
        public bool IsCtrlHeld => _isCtrlHeld;

        private Vector2 _frameCanvasPan = Vector2.Zero;
        private Vector2 _frameSpritePosition = Vector2.Zero;
        private Vector2 _frameDragStartPointer;
        private Vector2 _frameDragStartPan;
        private bool _isFrameCanvasDragging;
        private float _frameCanvasZoom = 1.0f;
        private const float FrameCanvasMinZoom = 0.2f;
        private const float FrameCanvasMaxZoom = 8.0f;
        private const double FrameSpriteBaseSize = 48.0;
        private readonly SolidColorBrush _frameCheckerLightBrush = new(Color.FromArgb(255, 238, 238, 238));
        private readonly SolidColorBrush _frameCheckerDarkBrush = new(Color.FromArgb(255, 206, 206, 206));

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TreeItem))]
        public MainWindow()
        {
            InitializeComponent();
            // Ensure the root grid is focusable and receives keyboard events
            this.Activated += (s, e) => MainRootGrid.Focus(FocusState.Programmatic);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 625));
            AppWindow.SetIcon("Assets/icon.ico");

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 695;
            presenter.PreferredMinimumHeight = 400;

            AppWindow.SetPresenter(presenter);


            programConfig = LoadProgramConfig();


            SetUpTreeViewAndConfigs();

            Activated -= MainWindow_Activated;
            Activated += MainWindow_Activated;

            HeaderBreadcrumbBar.ItemsSource = BreadcrumbItems;

            HeaderBreadcrumbBar.ItemClicked -= BreadcrumbBar_ItemClicked;
            HeaderBreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;

            ProgramNameTextBlock.Text += GetCurrentVersion();
            InitializeFrameCoordinatePlane();

        
            CheckForUpdateIfNeeded();

       
            AppWindow.Closing += AppWindow_Closing;
        }

        async void CheckForUpdateIfNeeded()
        {
            var now = DateTime.UtcNow;

            if (programConfig.LastUpdateCheck.HasValue)
            {
                var lastCheck = programConfig.LastUpdateCheck.Value;

       
                if (lastCheck.Date == now.Date)
                    return;
            }

        
            programConfig.LastUpdateCheck = now;
    

            await CheckForUpdateAsync();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
       
            args.Cancel = true;

       
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                TreeViewControl.Focus(FocusState.Programmatic);
                SetInfoBar(InfoBarSeverity.Informational, "Saving", "The program will soon close");
                await Task.Delay(30);
                SaveAllConfigs();
                this.Close();

            });
        }


        public static bool IsNewer(string latest, string current)
        {
            latest = latest.TrimStart('v');

            Version latestVersion = new(latest);
            Version currentVersion = new(current);

            return latestVersion > currentVersion;
        }

        public static string GetCurrentVersion()
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        async Task CheckForUpdateAsync()
        {
            string current = GetCurrentVersion();

            string? latest = await UpdateChecker.GetLatestVersionAsync();




            if (latest != null && IsNewer(latest, current))
            {
                UpdateBadge.Visibility = Visibility.Visible;
                UpdateInfoBar.IsOpen = true;
            }
        }
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                

                if (activated)
                {
                    programConfig = LoadProgramConfig();
                    ReloadTreeViewAndConfigs();
                }
                activated = true;
                ReduceFileSizeCheckBox.IsChecked = programConfig.ReduceFileSize;
                WorkingPathTextBox.Text = programConfig.WorkingPath;
                AnimationsToggleSwitch.IsOn = !programConfig.Animations;
                ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
                AnimationsToggleSwitch.Toggled -= AnimationsToggleSwitch_Toggled;
                ReduceFileSizeCheckBox.Click += ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged += WorkingPathTextBox_LostFocus;
                AnimationsToggleSwitch.Toggled += AnimationsToggleSwitch_Toggled;

                ActivateDelayedAsync();

            }
            else
            {
                IsWindowActive = false;

                ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
                AnimationsToggleSwitch.Toggled -= AnimationsToggleSwitch_Toggled;
                WaitThenSave();
            }
        }

        async void ActivateDelayedAsync()
        {
            await Task.Delay(1);
            IsWindowActive = true;
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

            while (programConfig.SelectedNodePath!.Count > clickedIndex + 1)
            {
                programConfig.SelectedNodePath.RemoveAt(programConfig.SelectedNodePath.Count - 1);
            }

            TreeViewNode? selectedNode = null;
            foreach (TreeViewNode gameThemeNode in TreeViewControl.RootNodes)
            {
                if ((gameThemeNode.Content as TreeItem)!.Text == programConfig.SelectedNodePath[0])
                {
                    if (programConfig.SelectedNodePath.Count == 1)
                    {
                        TreeViewControl.SelectedNode = gameThemeNode;
                        selectedNode = gameThemeNode;
                        break;
                    }
                    else
                    {
                        foreach (TreeViewNode subjectNode in gameThemeNode.Children)
                        {
                            if ((subjectNode.Content as TreeItem)!.Text == programConfig.SelectedNodePath[1])
                            {
                                if (programConfig.SelectedNodePath.Count == 2)
                                {
                                    TreeViewControl.SelectedNode = subjectNode;
                                    selectedNode = subjectNode;
                                    break;
                                }
                                else
                                {
                                    foreach (TreeViewNode animationNode in subjectNode.Children)
                                    {
                                        if ((animationNode.Content as TreeItem)!.Text == programConfig.SelectedNodePath[2])
                                        {
                                            if (programConfig.SelectedNodePath.Count == 3)
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

            if (selectedNode != null)
            {
                _ = DisplayBreadcrumbSelectedPanelAsync(selectedNode);
            }
        }

        async Task DisplayBreadcrumbSelectedPanelAsync(TreeViewNode node)
        {
            TreeViewControl.Focus(FocusState.Programmatic);

            FadeOutAllPanels(false, programConfig.Animations);
      
            DisplayCorrectPanel(node, programConfig.Animations);
        }


        private bool _isSaveBarShowing = true;
        private async void AnimateSaveBarBorder(bool show)
        {
            if (_isSaveBarShowing == show) return;

    
            _isSaveBarShowing = show;
            var compositor = ElementCompositionPreview.GetElementVisual(SaveBarBorder).Compositor;
            var saveBarVisual = ElementCompositionPreview.GetElementVisual(SaveBarBorder);
            var bottomPanelVisual = ElementCompositionPreview.GetElementVisual(BottomBarStackPanel);
 
            float height = (float)SaveBarBorder.ActualHeight;
            if (height <= 0) height = 100f; 

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



                // Fade out SaveBarBorder

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

        private async void WorkingPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAllConfigs();
            programConfig.WorkingPath = (sender as TextBox)!.Text;
            ReloadTreeViewAndConfigs();
            
        }

        private void AnimationsToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            programConfig.Animations = !(sender as ToggleSwitch)!.IsOn;
            
        }

        private void ReduceFileSizeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            programConfig.ReduceFileSize = (sender as CheckBox)!.IsChecked!.Value;
        }

        async void WaitThenSave()
        {

            TreeViewControl.Focus(FocusState.Programmatic);

            await Task.Delay(30);

            SaveAllConfigs();
        }


        void SaveAllConfigs()
        {
            if (hierarchyError)
            {
                SaveProgramConfig();
                return;
            }

            if (usingGameThemes)
            {
                SaveProgramConfig();
                var gameThemeDirs = Directory.GetDirectories(workingPath);

                foreach (var gameThemeDir in gameThemeDirs)
                {
                    string gameThemeName = Path.GetFileName(gameThemeDir);
                    var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);

                    SaveJson(gameThemeConfigPath, programConfig.GameThemeConfigs![gameThemeName]);
                    SaveSubjects(gameThemeDir, gameThemeName);
                }
            }
            else
            {
                programConfig.IsHd = programConfig.GameThemeConfigs!["Game Theme"].IsHd;
                SaveProgramConfig();
                SaveSubjects(workingPath, "Game Theme");
            }
        }

        void SaveSubjects(string gameThemeDir, string gameThemeName)
        {
            var subjectDirs = Directory.GetDirectories(gameThemeDir);
            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);
                var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);

                SaveJson(subjectConfigPath, programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName]);

                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                foreach (var animationDir in animationDirs)
                {
                    string animationName = Path.GetFileName(animationDir);

                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);

                    SaveJson(animationConfigPath, programConfig.GameThemeConfigs[gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName]);
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
            SaveJson(Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME), programConfig);
        }

        void ReloadTreeViewAndConfigs()
        {
            programConfig.GameThemeConfigs = [];
            TreeViewControl.RootNodes.Clear();
            TryCloseInfoBar();
            SetUpTreeViewAndConfigs();
        }

        void TryCloseInfoBar()
        {
            if (!PrimaryInfoBar.IsClosable && PrimaryInfoBar.Title != "Generating")
            {
                PrimaryInfoBar.IsOpen = false;
                SaveBarBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
            }
        }
        TreeViewNode lastSelectedNode = null;
        void SetUpTreeViewAndConfigs()
        {
            lastSelectedNode = null;
            hierarchyError = true;
            if (TreeViewControl != null)
            {
                TreeViewControl.ItemInvoked -= TreeViewControl_ItemInvoked;
                TreeViewControl.PointerPressed -= TreeViewControl_PointerPressed;
                TreeViewControl.Expanding -= TreeViewControl_Expanding;
                TreeViewControl.Collapsed -= TreeViewControl_Collapsed;

                TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;
                TreeViewControl.PointerPressed += TreeViewControl_PointerPressed;
                TreeViewControl.Expanding += TreeViewControl_Expanding;
                TreeViewControl.Collapsed += TreeViewControl_Collapsed;

                if (TreeViewControl.RootNodes.Count == 0)
                {
                    workingPath = AppContext.BaseDirectory;
                    if (!string.IsNullOrWhiteSpace(programConfig.WorkingPath))
                    {
                        workingPath = programConfig.WorkingPath;
                    }

                    if (!Directory.Exists(workingPath))
                    {
                        SetInfoBar(InfoBarSeverity.Error, "Working path is incorrect", $"Working path does not exist:\n{workingPath}", false);
                        TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                        TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                        OpenSettings();
                        return;
                    }

                    if (Directory.GetDirectories(workingPath).Length == 0)
                    {
                        TreeViewPlaceHolderText.Text = "Empty working directory";
                        TreeViewPlaceHolderButton.Visibility = Visibility.Visible;
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                        OpenSettings();
                        return;
                    }
                    usingGameThemes = IsUsingGameThemes();
                    if (usingGameThemes)
                    {
                        hierarchyError = false;
               
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath);

                            SetUpSubjectTreeViewAndConfigs(gameThemeDir, gameThemeName, gameThemeConfig);
                        }

                        if (lastSelectedNode == null)
                        {
                            OpenSettings();
                        }
                        else
                        {
                            WaitThenDisplayCorrectPanel(lastSelectedNode, false, true);
                        }
                    }
                    else
                    {
                        if (AreSubjectsCorrect(workingPath))
                        {
                            hierarchyError = false;
                     
                            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;

                            GameThemeConfig gameThemeConfig = new(programConfig.IsHd, true);

                            SetUpSubjectTreeViewAndConfigs(workingPath, "Game Theme", gameThemeConfig);

                            if (lastSelectedNode == null)
                            {
                                OpenSettings();
                            }
                            else
                            {
                                WaitThenDisplayCorrectPanel(lastSelectedNode, false, true);
                            }
                        }
                        else
                        {
                    
                            SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong...", false);
                            TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                            TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                            OpenSettings();
                        }
                    }
                    if(lastSelectedNode != null)
                        TreeViewControl.SelectedNode = lastSelectedNode;

                }
       
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

                    if (animationConfig.frameCongfigs == null)
                        animationConfig.frameCongfigs = [];

                    foreach (var frameFile in frameFiles)
                    {
                        if(Path.GetExtension(frameFile) != ".json")
                        {
                            if(animationConfig.frameCongfigs.Count < fileCount)
                            {
                                animationConfig.frameCongfigs.Add(new FrameConfig());
                            }

                            string frameName = frameIndex.ToString("D3");
                            TreeItem frameTreeItem = new(frameName, ItemDepth.Frame);
                            var frameTreeViewNode = new TreeViewNode { Content = frameTreeItem };




                            animationTreeItem.Children.Add(frameTreeViewNode);

                            if (programConfig.SelectedNodePath != null &&
                                programConfig.SelectedNodes != null &&
                                programConfig.SelectedNodePath.Count == 3 &&
                                programConfig.SelectedNodePath[0] == gameThemeName &&
                                programConfig.SelectedNodePath[1] == subjectName &&
                                programConfig.SelectedNodePath[2] == animationName &&
                                programConfig.SelectedNodes.Contains(frameName))
                            {

                                TreeViewControl.SelectedNode = frameTreeViewNode;
                                (TreeViewControl.SelectedNode.Content as TreeItem).IsSelected = true;
                                TreeViewControl.SelectedNode = null;

                                if (programConfig.LastSelectedNode == frameName)
                                {
                                    lastSelectedNode = frameTreeViewNode;
                                }
                            }

                            frameIndex++;
                        }
                    }

                    subjectConfig.AnimationConfigs![animationName] = animationConfig;
                    subjectTreeItem.Children.Add(animationTreeItem);

                    if (programConfig.SelectedNodePath != null &&
                        programConfig.SelectedNodes != null &&
                        programConfig.SelectedNodePath.Count == 2 &&
                        programConfig.SelectedNodePath[0] == gameThemeName &&
                        programConfig.SelectedNodePath[1] == subjectName &&
                        programConfig.SelectedNodes.Contains(animationName))
                    {

                        TreeViewControl.SelectedNode = animationTreeItem;
                        (TreeViewControl.SelectedNode.Content as TreeItem).IsSelected = true;
                        TreeViewControl.SelectedNode = null;

                        if (programConfig.LastSelectedNode == animationName)
                        {
                            lastSelectedNode = animationTreeItem;
                        }
                    }
                }

                (subjectTreeItem.Content as TreeItem).Count = framesSum;
                (subjectTreeItem.Content as TreeItem).CountText = framesSum.ToString();

                gameThemeConfig.SubjectConfigs![subjectName] = subjectConfig;

                gameThemeTreeItem.Children.Add(subjectTreeItem);

                if (programConfig.SelectedNodePath != null &&
                programConfig.SelectedNodes != null &&
                programConfig.SelectedNodePath.Count == 1 &&
                programConfig.SelectedNodePath[0] == gameThemeName &&
                programConfig.SelectedNodes.Contains(subjectName))
                {
                    TreeViewControl.SelectedNode = subjectTreeItem;
                    (TreeViewControl.SelectedNode.Content as TreeItem).IsSelected = true;
                    TreeViewControl.SelectedNode = null;

                    if (programConfig.LastSelectedNode == subjectName)
                    {
                        lastSelectedNode = subjectTreeItem;
                    }
                }
            }

            programConfig.GameThemeConfigs![gameThemeName] = gameThemeConfig;

            TreeViewControl.RootNodes.Add(gameThemeTreeItem);

            if (programConfig.SelectedNodes != null &&
                (programConfig.SelectedNodePath == null || programConfig.SelectedNodePath.Count == 0) &&
                programConfig.SelectedNodes.Contains(gameThemeName))
            {
                TreeViewControl.SelectedNode = gameThemeTreeItem;
                (TreeViewControl.SelectedNode.Content as TreeItem).IsSelected = true;
                TreeViewControl.SelectedNode = null;

                if (programConfig.LastSelectedNode == gameThemeName)
                {
                    lastSelectedNode = gameThemeTreeItem;
                }
            }

            
        }

        void CanGenerate(bool isEnabled)
        {
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


        private static bool IsUsingGameThemes()
        {
            try
            {
                var firstLevelDirs = Directory.GetDirectories(workingPath);

                foreach (var first in firstLevelDirs)
                {
                    if (!AreSubjectsCorrect(first))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
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


            var selectedNodesCount = programConfig.SelectedNodes?.Count ?? 0;

            if (
                (selectedNodesCount <= 1 && TreeViewControl.SelectedNode != node) ||
                (selectedNodesCount > 1 && (!IsCtrlHeld || TreeViewControl.SelectedNode != node))
            )
            {
                WaitThenDisplayCorrectPanel(node, programConfig.Animations);
            }

        }

        void WaitThenDisplayCorrectPanel(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            TreeViewControl.Focus(FocusState.Programmatic);
   
            SettingsToggleButton.IsChecked = false;
            ItemDepth depth = (node.Content as TreeItem).Depth;
            bool sameDepth = false;
            if(programConfig.SelectedNodes != null && (
               depth == ItemDepth.GameTheme && programConfig.SelectedNodePath.Count == 0 ||
               depth == ItemDepth.Subject && programConfig.SelectedNodePath.Count == 1 ||
               depth == ItemDepth.Animation && programConfig.SelectedNodePath.Count == 2 ||
               depth == ItemDepth.Frame && programConfig.SelectedNodePath.Count == 3))
            {
                sameDepth = true;
            }

            FadeOutAllPanels(sameDepth, animate);        
            DisplayCorrectPanel(node, animate, nowGenerated);
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

                        //await Task.Delay(30);
                        
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
                Duration = new Duration(TimeSpan.FromMilliseconds(fadeOutMs)),
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
                Duration = new Duration(TimeSpan.FromMilliseconds(fadeInMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacityAnimation, panel);
            Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
            storyboard.Children.Add(opacityAnimation);

            // Translate Y animation (slide up from 20px below)
            var translateAnimation = new DoubleAnimation
            {
                From = 10.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(fadeInMs)),
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
            
            if ((!programConfig.SelectedNodePath.SequenceEqual(newSelectedNodePath) || !IsCtrlHeld) && !nowGenerated)
            {
                programConfig.SelectedNodes = [];
                ClearAllTreeItemSelections();
            }
            programConfig.SelectedNodePath = newSelectedNodePath;
            
            programConfig.SelectedNodes.Add(selectedNode.Text);

            if(!nowGenerated)
               programConfig.LastSelectedNode = selectedNode.Text;

            selectedNode.IsSelected = true;
        }

        async void DisplayCorrectPanel(TreeViewNode node, bool animate = true, bool nowGenerated = false)
        {
            

            string gameThemeName;
            string subjectName;
            

            TreeItem selectedNode = null;

            

            ItemDepth depth = (node.Content as TreeItem).Depth;
            switch (depth)
            {
                case ItemDepth.GameTheme:
        
                    AnimateSaveBarBorder(show: false);
                    gameThemeName = ((node.Content as TreeItem)!).Text;                          
                   
                    selectedNode = (node.Content as TreeItem);

                    HandleSelection(selectedNode, nowGenerated, []);

                    var listOfSelections = programConfig.SelectedNodes.OrderBy(s => s);
                    UpdateBreadcrumb(string.Join(", ", listOfSelections));

                    TryCloseInfoBar();

                    if (animate)
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        FadeInPanel(GameThemePanel);
                    }
                    else
                    {
                        await Task.Delay(30);
                        DetachAllPanelEvents();
                        GameThemePanel.Visibility = Visibility.Visible;
                    }
                    currentConfigs = [];

                    var gameThemeConfig = programConfig.GameThemeConfigs![gameThemeName];

                    

                    foreach(string selectedNodeName in programConfig.SelectedNodes)
                    {
                        currentConfigs.Add(programConfig.GameThemeConfigs![selectedNodeName]);
                    }

                    
                    IsHdCheckBox.IsChecked = gameThemeConfig.IsHd;
                    IsHdCheckBox.Click += ClickIsHdCheckBox;
                    break;
                case ItemDepth.Subject:
                    AnimateSaveBarBorder(show: true);
                    gameThemeName = (node.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Content as TreeItem)!.Text;
                    selectedNode = (node.Content as TreeItem);

                    HandleSelection(selectedNode, nowGenerated, [gameThemeName]);
             

                    UpdateBreadcrumb(gameThemeName, string.Join(", ", programConfig.SelectedNodes.OrderBy(s => s)));

                    CheckFrameCountAndDisplayWarning((node.Content as TreeItem).Count);

                    GenerateButton.Content = $"Generate {selectedNode.Text}";

                    if (animate)
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        FadeInPanel(SubjectPanel);
                    }
                    else
                    {
                        await Task.Delay(30);
                        DetachAllPanelEvents();
                        SubjectPanel.Visibility = Visibility.Visible;
                    }

                    currentConfigs = [];
                    var subjectConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName];

                    foreach (string selectedNodeName in programConfig.SelectedNodes)
                    {
                        currentConfigs.Add(programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![selectedNodeName]);
                    }

                    subjectConfig.Sheet ??= new SheetConfig();

                    RemoveBackgroundCheckBox.IsChecked = subjectConfig.RemoveBackground;
                    CropSpritesCheckBox.IsChecked = subjectConfig.CropSprites;

                    ResizeTextBox.Text = subjectConfig.ResizeToPercent.ToString();
                    ColorTextBox.Text = subjectConfig.BackgroundColor;
                    ThresholdTextBox.Text = subjectConfig.ColorTreshold.ToString();
                    SheetWidthTextBox.Text = subjectConfig.Sheet.Width.ToString();
                    SheetHeightTextBox.Text = subjectConfig.Sheet.Height.ToString();

                    _isSettingBackgroundColor = true;
                    ColorTextBox.Text = subjectConfig.BackgroundColor ?? "";
                    UpdateColorPreviewFromText(subjectConfig.BackgroundColor);
                    _isSettingBackgroundColor = false;

                    RemoveBackgroundCheckBox.Click += ClickRemoveBackground;
                    CropSpritesCheckBox.Click += ClickCropSpritesCheckBox;

                    ResizeTextBox.ValueChanged += ResizeTextBox_ValueChanged;
                    ColorTextBox.TextChanged += ColorTextBox_TextChanged;
                    ColorTextBox.LostFocus += ColorTextBox_LostFocus_ReturnToLastValid;
                    ThresholdTextBox.ValueChanged += ThresholdTextBox_ValueChanged;

                    SheetWidthTextBox.ValueChanged += SheetWidthTextBox_ValueChanged;
                    SheetHeightTextBox.ValueChanged += SheetHeightTextBox_ValueChanged;


                    break;
                case ItemDepth.Animation:
                    AnimateSaveBarBorder(show: true);
                    gameThemeName = (node.Parent.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Parent.Content as TreeItem)!.Text;
                    string animationName = (node.Content as TreeItem)!.Text;
                    

                    selectedNode = (node.Content as TreeItem);

                    HandleSelection(selectedNode, nowGenerated, [gameThemeName, subjectName]);

                    UpdateBreadcrumb(gameThemeName, subjectName, string.Join(", ", programConfig.SelectedNodes.OrderBy(s => s)));

                    CheckFrameCountAndDisplayWarning((node.Parent.Content as TreeItem).Count);

                    GenerateButton.Content = $"Generate {programConfig.SelectedNodePath[1]}";

                    if (animate)
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        FadeInPanel(AnimationsPanel);
                    }
                    else
                    {
                        await Task.Delay(30);
                        DetachAllPanelEvents();
                        AnimationsPanel.Visibility = Visibility.Visible;
                    }
                    currentConfigs = [];
                    var animationConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName];

                    foreach (string selectedNodeName in programConfig.SelectedNodes)
                    {
                        currentConfigs.Add(programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![selectedNodeName]);
                    }
      
                    animationConfig.RecoverCroppedOffset ??= new RecoverCroppedOffset();
                    animationConfig.Offset ??= new Vector2(0, 0);

                    RegenerateCheckBox.IsChecked = animationConfig.Regenerate;
                    RecoverXCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.X;
                    RecoverYCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.Y;

                    DelayTextBox.Text = animationConfig.Delay.ToString();
                    OffsetXTextBox.Text = animationConfig.Offset.Value.X.ToString();
                    OffsetYTextBox.Text = animationConfig.Offset.Value.Y.ToString();

                    RegenerateCheckBox.Click += ClickRegenerateCheckBox;
                    RecoverXCheckBox.Click += ClickRecoverXCheckBox;
                    RecoverYCheckBox.Click += ClickRecoverYCheckBox;

                    DelayTextBox.ValueChanged += DelayTextBox_ValueChanged;
                    OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
                    OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;



                    break;
                case ItemDepth.Frame:
                    AnimateSaveBarBorder(show: true);
                    gameThemeName = (node.Parent.Parent.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Parent.Parent.Content as TreeItem)!.Text;
                    animationName = (node.Parent.Content as TreeItem)!.Text;
                    string frameName = (node.Content as TreeItem)!.Text;


                    selectedNode = (node.Content as TreeItem);

                    HandleSelection(selectedNode, nowGenerated, [gameThemeName, subjectName, animationName]);

                    UpdateBreadcrumb(gameThemeName, subjectName, animationName, string.Join(", ", programConfig.SelectedNodes.OrderBy(s => s)));

                    CheckFrameCountAndDisplayWarning((node.Parent.Parent.Content as TreeItem).Count);

                    GenerateButton.Content = $"Generate {programConfig.SelectedNodePath[1]}";

                    if (animate)
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        FadeInPanel(FramePanel);
                    }
                    else
                    {
                        await Task.Delay(30);
                        DetachAllPanelEvents();
                        FramePanel.Visibility = Visibility.Visible;
                    }
                    currentConfigs = [];
                    var frameConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].frameCongfigs[int.Parse(frameName)];

                    foreach (string selectedNodeName in programConfig.SelectedNodes)
                    {
                        currentConfigs.Add(programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName].frameCongfigs[int.Parse(selectedNodeName)]);
                    }

                   



                    break;
                default:
                    DetachAllPanelEvents();
                    break;
            }



        }

        private void InitializeFrameCoordinatePlane()
        {
            _frameCanvasPan = Vector2.Zero;
            _frameSpritePosition = Vector2.Zero;
            _isFrameCanvasDragging = false;
            _frameCanvasZoom = 1.0f;
            FrameVectorXTextBox.Value = 0;
            FrameVectorYTextBox.Value = 0;

            UpdateFrameCoordinateVisuals();
        }

        private void UpdateFrameCoordinateVisuals()
        {
            if (FrameCoordinateCanvas == null)
            {
                return;
            }

            double canvasWidth = FrameCoordinateCanvas.ActualWidth;
            double canvasHeight = FrameCoordinateCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                return;
            }

            double centerX = canvasWidth / 2.0;
            double centerY = canvasHeight / 2.0;

            double axisX = centerX + _frameCanvasPan.X;
            double axisY = centerY + _frameCanvasPan.Y;

            UpdateFrameCheckerboard(canvasWidth, canvasHeight, axisX, axisY);

            FrameXAxis.Width = canvasWidth;
            Canvas.SetLeft(FrameXAxis, 0);
            Canvas.SetTop(FrameXAxis, axisY - (FrameXAxis.Height / 2.0));

            FrameYAxis.Height = canvasHeight;
            Canvas.SetLeft(FrameYAxis, axisX - (FrameYAxis.Width / 2.0));
            Canvas.SetTop(FrameYAxis, 0);

            double spriteCanvasX = axisX + (_frameSpritePosition.X * _frameCanvasZoom);
            double spriteCanvasY = axisY - (_frameSpritePosition.Y * _frameCanvasZoom);

            double spriteSize = FrameSpriteBaseSize * _frameCanvasZoom;
            FrameCoordinateSpriteImage.Width = spriteSize;
            FrameCoordinateSpriteImage.Height = spriteSize;

            Canvas.SetLeft(FrameCoordinateSpriteImage, spriteCanvasX - (spriteSize / 2.0));
            Canvas.SetTop(FrameCoordinateSpriteImage, spriteCanvasY - (spriteSize / 2.0));

            FrameZoomTextBlock.Text = $"Zoom: {(int)Math.Round(_frameCanvasZoom * 100)}%";
        }

        private void UpdateFrameCheckerboard(double canvasWidth, double canvasHeight, double axisX, double axisY)
        {
            FrameCheckerboardCanvas.Width = canvasWidth;
            FrameCheckerboardCanvas.Height = canvasHeight;
            FrameCheckerboardCanvas.Children.Clear();

            double tileSize = 4.0 * _frameCanvasZoom;
            if (tileSize <= 0)
            {
                return;
            }

            int minTileX = (int)Math.Floor((-axisX) / tileSize) - 1;
            int maxTileX = (int)Math.Ceiling((canvasWidth - axisX) / tileSize) + 1;
            int minTileY = (int)Math.Floor((axisY - canvasHeight) / tileSize) - 1;
            int maxTileY = (int)Math.Ceiling(axisY / tileSize) + 1;

            for (int tileY = minTileY; tileY <= maxTileY; tileY++)
            {
                for (int tileX = minTileX; tileX <= maxTileX; tileX++)
                {
                    var tile = new Rectangle
                    {
                        Width = tileSize,
                        Height = tileSize,
                        Fill = ((tileX + tileY) & 1) == 0 ? _frameCheckerLightBrush : _frameCheckerDarkBrush
                    };

                    Canvas.SetLeft(tile, axisX + (tileX * tileSize));
                    Canvas.SetTop(tile, axisY - ((tileY + 1) * tileSize));
                    FrameCheckerboardCanvas.Children.Add(tile);
                }
            }
        }

        private void FrameCoordinateCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateFrameCoordinateVisuals();
        }

        private void FrameCoordinateCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(FrameCoordinateCanvas);
            _frameDragStartPointer = new Vector2((float)point.Position.X, (float)point.Position.Y);
            _frameDragStartPan = _frameCanvasPan;
            _isFrameCanvasDragging = true;

            FrameCoordinateCanvas.CapturePointer(e.Pointer);
        }

        private void FrameCoordinateCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(FrameCoordinateCanvas);
            var currentPosition = new Vector2((float)point.Position.X, (float)point.Position.Y);

            if (_isFrameCanvasDragging)
            {
                _frameCanvasPan = _frameDragStartPan + (currentPosition - _frameDragStartPointer);
                UpdateFrameCoordinateVisuals();
            }
        }

        private void FrameCoordinateCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isFrameCanvasDragging = false;
            FrameCoordinateCanvas.ReleasePointerCapture(e.Pointer);
        }

        private void FrameCoordinateCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(FrameCoordinateCanvas);
            int wheelDelta = point.Properties.MouseWheelDelta;
            if (wheelDelta == 0)
            {
                return;
            }

            float oldZoom = _frameCanvasZoom;
            float zoomMultiplier = wheelDelta > 0 ? 1.1f : 0.9f;
            float newZoom = Math.Clamp(oldZoom * zoomMultiplier, FrameCanvasMinZoom, FrameCanvasMaxZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.0001f)
            {
                return;
            }

            double canvasWidth = FrameCoordinateCanvas.ActualWidth;
            double canvasHeight = FrameCoordinateCanvas.ActualHeight;
            double centerX = canvasWidth / 2.0;
            double centerY = canvasHeight / 2.0;

            double oldAxisX = centerX + _frameCanvasPan.X;
            double oldAxisY = centerY + _frameCanvasPan.Y;

            double worldXUnderPointer = (point.Position.X - oldAxisX) / oldZoom;
            double worldYUnderPointer = (oldAxisY - point.Position.Y) / oldZoom;

            _frameCanvasZoom = newZoom;

            double newAxisX = point.Position.X - (worldXUnderPointer * newZoom);
            double newAxisY = point.Position.Y + (worldYUnderPointer * newZoom);

            _frameCanvasPan = new Vector2(
                (float)(newAxisX - centerX),
                (float)(newAxisY - centerY)
            );

            UpdateFrameCoordinateVisuals();
            e.Handled = true;
        }

        private void CenterFrameOriginButton_Click(object sender, RoutedEventArgs e)
        {
            _frameCanvasPan = Vector2.Zero;
            UpdateFrameCoordinateVisuals();
        }

        private void FrameVectorXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _frameSpritePosition = new Vector2(double.IsNaN(sender.Value) ? 0 : (float)sender.Value, _frameSpritePosition.Y);
            UpdateFrameCoordinateVisuals();
        }

        private void FrameVectorYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _frameSpritePosition = new Vector2(_frameSpritePosition.X, double.IsNaN(sender.Value) ? 0 : (float)sender.Value);
            UpdateFrameCoordinateVisuals();
        }

        private void DetachAllPanelEvents()
        {
            IsHdCheckBox.Click -= ClickIsHdCheckBox;
            RemoveBackgroundCheckBox.Click -= ClickRemoveBackground;
            CropSpritesCheckBox.Click -= ClickCropSpritesCheckBox;

            ResizeTextBox.ValueChanged -= ResizeTextBox_ValueChanged;
            ColorTextBox.TextChanged -= ColorTextBox_TextChanged;
            ColorTextBox.LostFocus -= ColorTextBox_LostFocus_ReturnToLastValid;
            ThresholdTextBox.ValueChanged -= ThresholdTextBox_ValueChanged;

            SheetWidthTextBox.ValueChanged -= SheetWidthTextBox_ValueChanged;
            SheetHeightTextBox.ValueChanged -= SheetHeightTextBox_ValueChanged;

            RegenerateCheckBox.Click -= ClickRegenerateCheckBox;
            RecoverXCheckBox.Click -= ClickRecoverXCheckBox;
            RecoverYCheckBox.Click -= ClickRecoverYCheckBox;

            DelayTextBox.ValueChanged -= DelayTextBox_ValueChanged;
            OffsetXTextBox.ValueChanged -= OffsetXTextBox_ValueChanged;
            OffsetYTextBox.ValueChanged -= OffsetYTextBox_ValueChanged;
        }

        private void OffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig!.Offset = new Vector2((currentConfig).Offset!.Value.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value);
            }

            
        }

        private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig.Offset = new Vector2(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, (currentConfig).Offset!.Value.Y);
            }
        }

        private void DelayTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig.Delay = double.IsNaN(sender.Value) ? 1 : (int)sender.Value;
            }
        }

        private void SheetHeightTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.Sheet.Height = double.IsNaN(sender.Value) ? null : (int)sender.Value;
            }
        }

        private void SheetWidthTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.Sheet.Width = double.IsNaN(sender.Value) ? null : (int)sender.Value;
            }
        }

        private void ThresholdTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.ColorTreshold = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
            }
        }

        private void ResizeTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.ResizeToPercent = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
            }
        }

        private void ClickRecoverYCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig.RecoverCroppedOffset.Y = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickRecoverXCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig.RecoverCroppedOffset.X = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickRegenerateCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (AnimationConfig currentConfig in currentConfigs)
            {
                currentConfig.Regenerate = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickCropSpritesCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.CropSprites = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickRemoveBackground(object sender, RoutedEventArgs e)
        {
            foreach (SubjectConfig currentConfig in currentConfigs)
            {
                currentConfig.RemoveBackground = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void ClickIsHdCheckBox(object sender, RoutedEventArgs e)
        {
            foreach (GameThemeConfig currentConfig in currentConfigs)
            {
                currentConfig.IsHd = (sender as CheckBox)!.IsChecked!.Value;
            }
        }

        private void TreeViewControl_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;

            ItemDepth depth = (node.Content as TreeItem).Depth;
            switch (depth)
            {
                case ItemDepth.GameTheme:
                    programConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                case ItemDepth.Subject:
                    programConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                case ItemDepth.Animation:
                    programConfig.GameThemeConfigs![(node.Parent.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs[(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                default:
                    break;
            }
        }

        private void TreeViewControl_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            TreeViewNode node = args.Node;
            ItemDepth depth = (node.Content as TreeItem).Depth;

            switch (depth)
            {
                case ItemDepth.GameTheme:
                    programConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;
                case ItemDepth.Subject:
                    programConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;
                case ItemDepth.Animation:
                    programConfig.GameThemeConfigs![(node.Parent.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Parent.Content as TreeItem)!.Text].AnimationConfigs[(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;
                default:
                    break;
            }
        }



        private void ClickSettings(object sender, RoutedEventArgs e)
        {
            _ = OpenSettingsAsync();
        }

        async Task OpenSettingsAsync()
        {
            SettingsToggleButton.IsChecked = true;
            ClearAllTreeItemSelections();
            
            if (HelpPanel.Visibility == Visibility.Visible)
            {
                return;
            }
            TreeViewControl.SelectedNode = null;
            programConfig.SelectedNodePath = [];
            programConfig.SelectedNodes = null;
            FadeOutAllPanels(false, programConfig.Animations);

            UpdateBreadcrumb("Settings & Help");
            AnimateSaveBarBorder(show: false);
            TryCloseInfoBar();
            if (programConfig.Animations)
            {
                await Task.Delay(fadeOutMs);
                FadeInPanel(HelpPanel);
            }
            else
            {
                await Task.Delay(30);
                HelpPanel.Visibility = Visibility.Visible;
            }
            

       
        

        }

        public void OpenSettings()
        {
            if (!activated)
            {
                SaveBarBorder.Opacity = 0;

                BottomBarStackPanel.LayoutUpdated += BottomBarStackPanel_LayoutUpdated;


            }

  

            _ = OpenSettingsAsync();
        }

        private void BottomBarStackPanel_LayoutUpdated(object sender, object e)
        {
            BottomBarStackPanel.LayoutUpdated -= BottomBarStackPanel_LayoutUpdated;

            var bottomPanelVisual = ElementCompositionPreview.GetElementVisual(BottomBarStackPanel);
            bottomPanelVisual.Offset = new Vector3(0, (float)SaveBarBorder.ActualHeight, 0);
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
                    player.Source = MediaSource.CreateFromUri(
                        new Uri("ms-winsoundevent:Notification.Default")
                    );
                    player.Play();
                    break;
                case InfoBarSeverity.Error:
                    player.Source = MediaSource.CreateFromUri(
                        new Uri("ms-winsoundevent:SystemExclamation")
                    );
                    player.Play();
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
            var gameThemePath = Path.Combine(workingPath, "GameTheme1");
            var subject1Path = Path.Combine(gameThemePath, "Subject1", "raw");
            var subject2Path = Path.Combine(gameThemePath, "Subject2", "raw");
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim2"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "Anim3"));

            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim1"));
            Directory.CreateDirectory(Path.Combine(subject2Path, "Anim2"));

            SetInfoBar(InfoBarSeverity.Success, "Example generated", "Rename your folders and fill up the animation folders with frames");
            ReloadTreeViewAndConfigs();
        }

        private void UpdateColorPreviewFromText(string? text)
        {
            if (TryNormalizeHexToColor(text, out string normalizedHex, out Windows.UI.Color color))
            {
                ColorPreviewBorder.Background = new SolidColorBrush(color);
                _lastValidBackgroundColor = normalizedHex;

                if (currentConfigs.First() is SubjectConfig sc)
                {
                    sc.BackgroundColor = normalizedHex;
                }

                ColorPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
            }
            else
            {
                ColorPreviewBorder.Background = new SolidColorBrush();
                ColorPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
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

        private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSettingBackgroundColor) return;

            var tb = sender as TextBox;
            if (tb == null) return;

            var text = tb.Text;
            UpdateColorPreviewFromText(text);
        }

        private void ColorTextBox_LostFocus_ReturnToLastValid(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            if (!TryNormalizeHexToColor(tb.Text, out _, out _))
            {
                if (string.IsNullOrEmpty(_lastValidBackgroundColor))
                {
                    tb.Text = "";
                    if (currentConfigs.First() is SubjectConfig sc) sc.BackgroundColor = null;
                    ColorPreviewBorder.Background = new SolidColorBrush();
                }
                else
                {
                    _isSettingBackgroundColor = true;
                    tb.Text = _lastValidBackgroundColor;
                    UpdateColorPreviewFromText(_lastValidBackgroundColor);
                    _isSettingBackgroundColor = false;
                }
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/releases/latest"));
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\A[0-9A-F]+\z")]
        private static partial System.Text.RegularExpressions.Regex ColorRegex();

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            List<String> fullSelectionPath = [];
            fullSelectionPath.AddRange(programConfig.SelectedNodePath);
            fullSelectionPath.Add(programConfig.LastSelectedNode);
            string gameThemeName = fullSelectionPath[0];
            string subjectName = fullSelectionPath[1];

            SetInfoBar(InfoBarSeverity.Informational, "Generating", $"{subjectName} is being generated", false);
            IsGenerating = true;
            await Task.Delay(30);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Run the processing on a background thread to avoid UI freeze
                await Task.Run(async () => await Processer.StartProcessAsync(gameThemeName, subjectName));
                stopwatch.Stop();
                SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"Spritesheet generated into {subjectName}/generated ({stopwatch.ElapsedMilliseconds}ms)");
            }
            catch (Exception er)
            {
                SetInfoBar(InfoBarSeverity.Error, "Generation failed", er.Message);
            }

            SaveAllConfigs();
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
                programConfig.WorkingPath = folder.Path;
                WaitThenSave();
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
                        ItemDepth depth = (node.Content as TreeItem).Depth;
                        switch (depth)
                        {
                            case ItemDepth.GameTheme:
                                configPath = Path.Combine(workingPath, ((node.Content as TreeItem)!).Text, "config.json");
                                break;
                            case ItemDepth.Subject:
                                configPath = Path.Combine(workingPath, ((node.Parent.Content as TreeItem)!).Text, ((node.Content as TreeItem)!).Text, "config.json");
                                break;
                            case ItemDepth.Animation:
                                configPath = Path.Combine(workingPath, ((node.Parent.Parent.Content as TreeItem)!).Text, ((node.Parent.Content as TreeItem)!).Text, "raw", ((node.Content as TreeItem)!).Text, "config.json");
                                break;
                        }
                        if (configPath != null && File.Exists(configPath))
                        {
                            await Windows.System.Launcher.LaunchUriAsync(new Uri("file:///" + configPath.Replace('\\', '/')));
                        }
                    }
                }
            }
        }

        private void BottomBarStackPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if ((HelpPanel.Children[0] as ScrollViewer) == null) return;
            var HelpStackPanel = ((HelpPanel.Children[0] as ScrollViewer).Content as StackPanel);
            HelpStackPanel.Padding = new Thickness(
                HelpStackPanel.Padding.Left,
                HelpStackPanel.Padding.Top,
                HelpStackPanel.Padding.Right,
                PrimaryInfoBar.ActualHeight + 12);

            var GameThemeStackPanel = ((GameThemePanel.Children[0] as ScrollViewer).Content as StackPanel);
            GameThemeStackPanel.Padding = new Thickness(
                GameThemeStackPanel.Padding.Left,
                GameThemeStackPanel.Padding.Top,
                GameThemeStackPanel.Padding.Right,
                PrimaryInfoBar.ActualHeight + 12);

            var SubjectStackPanel = ((SubjectPanel.Children[0] as ScrollViewer).Content as StackPanel);
            SubjectStackPanel.Padding = new Thickness(
                SubjectStackPanel.Padding.Left,
                SubjectStackPanel.Padding.Top,
                SubjectStackPanel.Padding.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);

            var AnimationStackPanel = ((AnimationsPanel.Children[0] as ScrollViewer).Content as StackPanel);
            AnimationStackPanel.Padding = new Thickness(
                AnimationStackPanel.Padding.Left,
                AnimationStackPanel.Padding.Top,
                AnimationStackPanel.Padding.Right,
                BottomBarStackPanel.ActualHeight + 12 * 2);
        }
        private async void ProgramDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var exeDir = AppContext.BaseDirectory;
            await Windows.System.Launcher.LaunchUriAsync(new Uri("file:///" + exeDir.Replace('\\', '/')));
        }

        private void MainRootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Control ||
                e.Key == Windows.System.VirtualKey.LeftControl ||
                e.Key == Windows.System.VirtualKey.RightControl)
            {
                _isCtrlHeld = true;
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
        }

        private void ClearAllTreeItemSelections()
        {
            foreach (TreeViewNode node in TreeViewControl.RootNodes)
            {
                ClearNodeSelection(node);
            }
        }

        private void ClearNodeSelection(TreeViewNode node)
        {
            (node.Content as TreeItem).IsSelected = false;
            foreach (TreeViewNode child in node.Children)
            {
                ClearNodeSelection(child);
            }
        }
        void ResizeList<T>(List<T> list, int targetSize, Func<T> createNew)
        {
            // ha túl hosszú → levágjuk
            if (list.Count > targetSize)
            {
                list.RemoveRange(targetSize, list.Count - targetSize);
            }

            // ha túl rövid → feltöltjük
            while (list.Count < targetSize)
            {
                list.Add(createNew());
            }
        }
    }


}
