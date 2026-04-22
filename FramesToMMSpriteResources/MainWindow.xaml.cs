using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
    public class TreeItem
    {
        public string Text { get; set; }
        public string IconGlyph { get; set; }

        public TreeItem(string text, string iconGlyph)
        {
            Text = text;
            IconGlyph = iconGlyph;
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
    enum ItemDepth
    {
        GameTheme = 0,
        Subject = 1,
        Animation = 2
    }

    public sealed partial class MainWindow : Window
    {
        private static readonly string CONFIG_FILENAME = "config.json";

        public static string workingPath = AppContext.BaseDirectory;

        public static ProgramConfig programConfig;
        private object currentConfig;

        bool activated = false;

        public static bool usingGameThemes = false;
        bool hierarchyError = true;

        private bool _isSettingBackgroundColor;
        private string _lastValidBackgroundColor = "";

        MediaPlayer player = new();

        int fadeOutMs = 50;
        int fadeInMs = 100;

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
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TreeItem))]
        public MainWindow()
        {
            InitializeComponent();






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

            //TODO: NAPONTA EGYSZER
            CheckForUpdate();

            // Subscribe to the cancellable Closing event
            AppWindow.Closing += AppWindow_Closing;
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // Immediately cancel the system close so we can show an async dialog.
            // (If you already know you want to cancel, set true and return.)
            args.Cancel = true;

            // Use the UI dispatcher to show an async dialog after the handler returns.
            // Do not await inside the event handler itself (the event is synchronous).
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

        async void CheckForUpdate()
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
                ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
                ReduceFileSizeCheckBox.Click += ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged += WorkingPathTextBox_LostFocus;

            }
            else
            {
                ReduceFileSizeCheckBox.Click -= ReduceFileSizeCheckBox_Click;
                WorkingPathTextBox.TextChanged -= WorkingPathTextBox_LostFocus;
                WaitThenSave();
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

            while (programConfig.SelectedNode!.Count > clickedIndex + 1)
            {
                programConfig.SelectedNode.RemoveAt(programConfig.SelectedNode.Count - 1);
            }

            TreeViewNode? selectedNode = null;
            foreach (TreeViewNode gameThemeNode in TreeViewControl.RootNodes)
            {
                if ((gameThemeNode.Content as TreeItem)!.Text == programConfig.SelectedNode[0])
                {
                    if (programConfig.SelectedNode.Count == 1)
                    {
                        TreeViewControl.SelectedNode = gameThemeNode;
                        selectedNode = gameThemeNode;
                        break;
                    }
                    else
                    {
                        foreach (TreeViewNode subjectNode in gameThemeNode.Children)
                        {
                            if ((subjectNode.Content as TreeItem)!.Text == programConfig.SelectedNode[1])
                            {
                                if (programConfig.SelectedNode.Count == 2)
                                {
                                    TreeViewControl.SelectedNode = subjectNode;
                                    selectedNode = subjectNode;
                                    break;
                                }
                                else
                                {
                                    foreach (TreeViewNode animationNode in subjectNode.Children)
                                    {
                                        if ((animationNode.Content as TreeItem)!.Text == programConfig.SelectedNode[2])
                                        {

                                            TreeViewControl.SelectedNode = animationNode;
                                            selectedNode = animationNode;
                                            break;

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

            FadeOutAllPanels(false);
            ItemDepth depth = GetNodeDepth(node);
            DisplayCorrectPanel(node, depth);
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

        private void WorkingPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            programConfig.WorkingPath = (sender as TextBox)!.Text;
            ReloadTreeViewAndConfigs();
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
            if (!PrimaryInfoBar.IsClosable && PrimaryInfoBar.Title != "Generating")
            {
                PrimaryInfoBar.IsOpen = false;
                SaveBarBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
            }
            SetUpTreeViewAndConfigs();
        }

        void SetUpTreeViewAndConfigs()
        {
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
                        UnallowGeneration();
                        OpenSettings();
                        return;
                    }

                    if (Directory.GetDirectories(workingPath).Length == 0)
                    {
                        TreeViewPlaceHolderText.Text = "Empty working directory";
                        TreeViewPlaceHolderButton.Visibility = Visibility.Visible;
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                        UnallowGeneration();
                        OpenSettings();
                        return;
                    }
                    usingGameThemes = IsUsingGameThemes();
                    if (usingGameThemes)
                    {
                        hierarchyError = false;
                        AllowGeneration();
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath);

                            SetUpSubjectTreeViewAndConfigs(gameThemeDir, gameThemeName, gameThemeConfig);
                        }

                        if (TreeViewControl.SelectedNode == null)
                        {
                            OpenSettings();
                        }
                        else
                        {
                            WaitThenDisplayCorrectPanel(TreeViewControl.SelectedNode, false);
                        }
                    }
                    else
                    {
                        if (AreSubjectsCorrect(workingPath))
                        {
                            hierarchyError = false;
                            AllowGeneration();
                            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;

                            GameThemeConfig gameThemeConfig = new(programConfig.IsHd, true);

                            SetUpSubjectTreeViewAndConfigs(workingPath, "Game Theme", gameThemeConfig);

                            if (TreeViewControl.SelectedNode == null)
                            {
                                OpenSettings();
                            }
                            else
                            {
                                WaitThenDisplayCorrectPanel(TreeViewControl.SelectedNode, false);
                            }
                        }
                        else
                        {
                            UnallowGeneration();
                            SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong...", false);
                            TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                            TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                            OpenSettings();
                        }
                    }
                }
            }
        }

        void SetUpSubjectTreeViewAndConfigs(string gameThemeDir, string gameThemeName, GameThemeConfig gameThemeConfig)
        {
            var gameThemeTreeItem = new TreeViewNode { Content = new TreeItem(gameThemeName, "\uE913"), IsExpanded = gameThemeConfig.IsExpanded };

            var subjectDirs = Directory.GetDirectories(gameThemeDir);

            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);

                var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);
                SubjectConfig subjectConfig = LoadJson<SubjectConfig>(subjectConfigPath);

                var subjectTreeItem = new TreeViewNode { Content = new TreeItem(subjectName, "\uF158"), IsExpanded = subjectConfig.IsExpanded };

                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                foreach (var animationDir in animationDirs)
                {
                    string animationName = Path.GetFileName(animationDir);

                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);
                    AnimationConfig animationConfig = LoadJson<AnimationConfig>(animationConfigPath);

                    subjectConfig.AnimationConfigs![animationName] = animationConfig;

                    var animationTreeItem = new TreeViewNode { Content = new TreeItem(animationName, "\uE805") };
                    subjectTreeItem.Children.Add(animationTreeItem);

                    if (programConfig.SelectedNode != null &&
                        programConfig.SelectedNode.Count == 3 &&
                        programConfig.SelectedNode[0] == gameThemeName &&
                        programConfig.SelectedNode[1] == subjectName &&
                        programConfig.SelectedNode[2] == animationName)
                    {
                        TreeViewControl.SelectedNode = animationTreeItem;
                    }
                }

                gameThemeConfig.SubjectConfigs![subjectName] = subjectConfig;

                gameThemeTreeItem.Children.Add(subjectTreeItem);

                if (programConfig.SelectedNode != null &&
                    programConfig.SelectedNode.Count == 2 &&
                    programConfig.SelectedNode[0] == gameThemeName &&
                    programConfig.SelectedNode[1] == subjectName)
                {
                    TreeViewControl.SelectedNode = subjectTreeItem;
                }
            }

            programConfig.GameThemeConfigs![gameThemeName] = gameThemeConfig;

            TreeViewControl.RootNodes.Add(gameThemeTreeItem);

            if (programConfig.SelectedNode != null &&
                programConfig.SelectedNode.Count == 1 &&
                programConfig.SelectedNode[0] == gameThemeName)
            {
                TreeViewControl.SelectedNode = gameThemeTreeItem;
            }
        }

        void UnallowGeneration()
        {
   
            ReduceFileSizeCheckBox.IsEnabled = false;
            GenerateButton.IsEnabled = false;
            ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
        }

        void AllowGeneration()
        {

            ReduceFileSizeCheckBox.IsEnabled = true;
            GenerateButton.IsEnabled = true;
            ReduceFileSizeCheckBoxTexts.Opacity = 1;
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
            if (TreeViewControl.SelectedNode == node) return;
            WaitThenDisplayCorrectPanel(node);
        }

        void WaitThenDisplayCorrectPanel(TreeViewNode node, bool animate = true)
        {
            TreeViewControl.Focus(FocusState.Programmatic);
   
            SettingsToggleButton.IsChecked = false;
            ItemDepth depth = GetNodeDepth(node);
            bool sameDepth = false;
            if(programConfig.SelectedNode != null && (
               depth == ItemDepth.GameTheme && programConfig.SelectedNode.Count == 1 ||
               depth == ItemDepth.Subject && programConfig.SelectedNode.Count == 2 ||
               depth == ItemDepth.Animation && programConfig.SelectedNode.Count == 3))
            {
                sameDepth = true;
            }

            FadeOutAllPanels(sameDepth, animate);        
            DisplayCorrectPanel(node, depth, animate);
        }

        void FadeOutAllPanels(bool sameDepth, bool animate = true)
        {
            var panels = new[] { GameThemePanel, SubjectPanel, AnimationsPanel, HelpPanel };

            foreach (var panel in panels)
            {
                if (panel.Visibility == Visibility.Visible)
                {
                    if (animate)
                    {
                        FadeOutPanel(panel, sameDepth);
                    }
                    else
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

        async void DisplayCorrectPanel(TreeViewNode node, ItemDepth depth, bool animate = true)
        {
      

         

            string gameThemeName;
            string subjectName;
         
            switch (depth)
            {
                case ItemDepth.GameTheme:
        
                    AnimateSaveBarBorder(show: false);
                    gameThemeName = ((node.Content as TreeItem)!).Text;         
                    UpdateBreadcrumb(gameThemeName);
                    programConfig.SelectedNode = [(node.Content as TreeItem)!.Text];
                    
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
                    
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName];
                    var gameThemeConfig = (currentConfig as GameThemeConfig)!;
                    IsHdCheckBox.IsChecked = gameThemeConfig.IsHd;
                    IsHdCheckBox.Click += ClickIsHdCheckBox;
                    break;
                case ItemDepth.Subject:
                    AnimateSaveBarBorder(show: true);
                    gameThemeName = (node.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Content as TreeItem)!.Text;
                    UpdateBreadcrumb(gameThemeName, subjectName);

                    programConfig.SelectedNode = [(node.Parent.Content as TreeItem)!.Text, (node.Content as TreeItem)!.Text];
                    if (animate)
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        FadeInPanel(SubjectPanel);
                    }
                    else
                    {
                        await Task.Delay(fadeOutMs);
                        DetachAllPanelEvents();
                        SubjectPanel.Visibility = Visibility.Visible;
                    }
                  
                 
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName];
           
                    var subjectConfig = (currentConfig as SubjectConfig)!;
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
                    UpdateBreadcrumb(gameThemeName, subjectName, animationName);

                    programConfig.SelectedNode = [(node.Parent.Parent.Content as TreeItem)!.Text, (node.Parent.Content as TreeItem)!.Text, (node.Content as TreeItem)!.Text];

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
        
           
                    
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName];
      
                    var animationConfig = (currentConfig as AnimationConfig)!;
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
                default:
                    DetachAllPanelEvents();
                    break;
            }


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
            (currentConfig as AnimationConfig)!.Offset = new Vector2((currentConfig as AnimationConfig)!.Offset!.Value.X, double.IsNaN(sender.Value) ? 0 : (int)sender.Value);
        }

        private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as AnimationConfig)!.Offset = new Vector2(double.IsNaN(sender.Value) ? 0 : (int)sender.Value, (currentConfig as AnimationConfig)!.Offset!.Value.Y);
        }

        private void DelayTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as AnimationConfig)!.Delay = double.IsNaN(sender.Value) ? 1 : (int)sender.Value;
        }

        private void SheetHeightTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.Sheet.Height = double.IsNaN(sender.Value) ? null : (int)sender.Value;
        }

        private void SheetWidthTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.Sheet.Width = double.IsNaN(sender.Value) ? null : (int)sender.Value;
        }

        private void ThresholdTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.ColorTreshold = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
        }

        private void ResizeTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.ResizeToPercent = double.IsNaN(sender.Value) ? 100 : (int)sender.Value;
        }

        private void ClickRecoverYCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.Y = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickRecoverXCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.X = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickRegenerateCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.Regenerate = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickCropSpritesCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as SubjectConfig)!.CropSprites = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickRemoveBackground(object sender, RoutedEventArgs e)
        {
            (currentConfig as SubjectConfig)!.RemoveBackground = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickIsHdCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as GameThemeConfig)!.IsHd = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void TreeViewControl_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;

            ItemDepth depth = GetNodeDepth(node);
            switch (depth)
            {
                case ItemDepth.GameTheme:
                    programConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                case ItemDepth.Subject:
                    programConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = true;
                    break;
                default:
                    break;
            }
        }

        private void TreeViewControl_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            TreeViewNode node = args.Node;
            ItemDepth depth = GetNodeDepth(node);

            switch (depth)
            {
                case ItemDepth.GameTheme:
                    programConfig.GameThemeConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;
                case ItemDepth.Subject:
                    programConfig.GameThemeConfigs![(node.Parent.Content as TreeItem)!.Text].SubjectConfigs![(node.Content as TreeItem)!.Text].IsExpanded = false;
                    break;
                default:
                    break;
            }
        }

        private static ItemDepth GetNodeDepth(TreeViewNode node)
        {
            int depth = -1;
            while (node.Parent != null)
            {
                depth++;
                node = node.Parent;
            }
            return (ItemDepth)depth;
        }

        private void ClickSettings(object sender, RoutedEventArgs e)
        {
            _ = OpenSettingsAsync();
        }

        async Task OpenSettingsAsync()
        {
           
            SettingsToggleButton.IsChecked = true;
            // Skip animation if settings are already open
            if (HelpPanel.Visibility == Visibility.Visible)
            {
                return;
            }
            TreeViewControl.SelectedNode = null;
            programConfig.SelectedNode = null;
            FadeOutAllPanels(false);

            UpdateBreadcrumb("Settings & Help");
            AnimateSaveBarBorder(show: false);
            await Task.Delay(fadeOutMs);
            FadeInPanel(HelpPanel);

       
        

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

                if (currentConfig is SubjectConfig sc)
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
                    if (currentConfig is SubjectConfig sc) sc.BackgroundColor = null;
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
            SetInfoBar(InfoBarSeverity.Informational, "Generating", $"{programConfig.SelectedNode![1]} is being generated", false);
            ControlEnabler.IsEnabled = false;
            HeaderBreadcrumbBar.IsEnabled = false;
            TreeViewControl.IsEnabled = false;
            SettingsToggleButton.IsEnabled = false;
            UnallowGeneration();
            await Task.Delay(30);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await Processer.StartProcessAsync();
                stopwatch.Stop();
                SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"Spritesheet generated into {programConfig.SelectedNode![1]}/generated ({stopwatch.ElapsedMilliseconds}ms)");

            }
            catch (Exception er)
            {
                SetInfoBar(InfoBarSeverity.Error, "Generation failed", er.Message);
            }
          
            ControlEnabler.IsEnabled = true;
            HeaderBreadcrumbBar.IsEnabled = true;
            TreeViewControl.IsEnabled = true;
            SettingsToggleButton.IsEnabled = true;
            AllowGeneration();
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
                        switch (GetNodeDepth(node))
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
    }
}
