using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.ApplicationModel;

using Windows.Media.Playback;
using Windows.Media.Core;

namespace FramesToMMSpriteResource
{
    public class TreeItem(string text, string iconGlyph)
    {
        public string Text { get; set; } = text;
        public string IconGlyph { get; set; } = iconGlyph;
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

        public ObservableCollection<string> BreadcrumbItems { get; } = new();

        private readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            IncludeFields = true
        };

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

            SaveBarBorder.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, SaveBarBorderVisibilityChanged);

            SetUpTreeViewAndConfigs();

            Activated -= MainWindow_Activated;
            Activated += MainWindow_Activated;

            HeaderBreadcrumbBar.ItemsSource = BreadcrumbItems;

            HeaderBreadcrumbBar.ItemClicked -= BreadcrumbBar_ItemClicked;
            HeaderBreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;

            ProgramNameTextBlock.Text += GetCurrentVersion();

            CheckForUpdate();

     
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
            TreeViewControl.Focus(FocusState.Programmatic);

            int clickedIndex = args.Index;
            
            while (programConfig.SelectedNode!.Count > clickedIndex + 1)
            {
                programConfig.SelectedNode.RemoveAt(programConfig.SelectedNode.Count - 1);
            }

            foreach (TreeViewNode gameThemeNode in TreeViewControl.RootNodes)
            {
                if((gameThemeNode.Content as TreeItem)!.Text == programConfig.SelectedNode[0])
                {
                    if(programConfig.SelectedNode.Count == 1)
                    {
                        TreeViewControl.SelectedNode = gameThemeNode;
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
                                    break;
                                }
                                else
                                {
                                    foreach (TreeViewNode animationNode in subjectNode.Children)
                                    {
                                        if ((animationNode.Content as TreeItem)!.Text == programConfig.SelectedNode[2])
                                        {
                                       
                                            TreeViewControl.SelectedNode = animationNode;
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

            WaitThenDisplayCorrectPanel(TreeViewControl.SelectedNode);
        }

        private void SaveBarBorderVisibilityChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (((UIElement)sender).Visibility == Visibility.Visible)
            {
                PrimaryInfoBar.CornerRadius = new CornerRadius(8, 8, 0, 0);
            }
            else
            {
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
            await Task.Delay(1);
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
                var filename = string.IsNullOrEmpty(filePath) ? "" : Path.GetFileName(filePath);
                SetInfoBar(InfoBarSeverity.Error, title, $"Could not load {filename}\n{ex.Message}");

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
            if (!PrimaryInfoBar.IsClosable)
            {
                PrimaryInfoBar.IsOpen = false;
            }
            SetUpTreeViewAndConfigs();
        }

        void SetUpTreeViewAndConfigs()
        {
            hierarchyError = true;
            if (TreeViewControl != null)
            {
                TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;
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
                        TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                        UnallowGeneration();
                        OpenSettings();
                        return;
                    }

                    if (Directory.GetDirectories(workingPath).Length == 0)
                    {
                        TreeViewPlaceHolderText.Text = "Empty working directory";
                        TreeViewPlaceHolderButton.Visibility = Visibility.Visible;
                        UnallowGeneration();
                        OpenSettings();
                        return;
                    }
                    usingGameThemes = IsUsingGameThemes();
                    if (usingGameThemes)
                    {
                        hierarchyError = false;
                        AllowGeneration();
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath);      

                            SetUpSubjectTreeViewAndConfigs(gameThemeDir, gameThemeName, gameThemeConfig);
                        }

                        if(TreeViewControl.SelectedNode == null)
                        {
                            OpenSettings();
                        }
                        else
                        {
                            DisplayCorrectPanel(TreeViewControl.SelectedNode);
                        }          
                    }
                    else
                    {
                        if (AreSubjectsCorrect(workingPath))
                        {
                            hierarchyError = false;
                            AllowGeneration();

                            GameThemeConfig gameThemeConfig = new(programConfig.IsHd, true);
                    
                            SetUpSubjectTreeViewAndConfigs(workingPath, "Game Theme", gameThemeConfig);

                            if (TreeViewControl.SelectedNode == null)
                            {
                                OpenSettings();
                            }
                            else
                            {
                                DisplayCorrectPanel(TreeViewControl.SelectedNode);
                            }
                        }
                        else
                        {
                            UnallowGeneration();
                            SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong...", false);
                            TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                            TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
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

                    var animationTreeItem = new TreeViewNode { Content = new TreeItem(animationName, "\uE805")};
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
            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
            ReduceFileSizeCheckBox.IsEnabled = false;
            GenerateButton.IsEnabled = false;
            ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
        }

        void AllowGeneration()
        {
            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;
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
            WaitThenDisplayCorrectPanel((args.InvokedItem as TreeViewNode)!);
        }

        async void WaitThenDisplayCorrectPanel(TreeViewNode node)
        {
            await Task.Delay(1);
            DisplayCorrectPanel(node);
        }

        void DisplayCorrectPanel(TreeViewNode node)
        {
            ItemDepth depth = GetNodeDepth(node);

            GameThemePanel.Visibility = Visibility.Collapsed;
            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Collapsed;

            DetachAllPanelEvents();

            string gameThemeName;
            string subjectName;
            switch (depth)
            {
                case ItemDepth.GameTheme: 
                    GameThemePanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Collapsed;
                    gameThemeName = ((node.Content as TreeItem)!).Text;
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName];
                    UpdateBreadcrumb(gameThemeName);
                    var gameThemeConfig = (currentConfig as GameThemeConfig)!;
                    IsHdCheckBox.IsChecked = gameThemeConfig.IsHd;
                    IsHdCheckBox.Click += ClickIsHdCheckBox;

                    programConfig.SelectedNode = [(node.Content as TreeItem)!.Text];
                    break;
                case ItemDepth.Subject: 
                    SubjectPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    gameThemeName = (node.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Content as TreeItem)!.Text;
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName];
                    UpdateBreadcrumb(gameThemeName, subjectName);
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

                    programConfig.SelectedNode = [(node.Parent.Content as TreeItem)!.Text, (node.Content as TreeItem)!.Text];
                    break;
                case ItemDepth.Animation:
                    AnimationsPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    gameThemeName = (node.Parent.Parent.Content as TreeItem)!.Text;
                    subjectName = (node.Parent.Content as TreeItem)!.Text;
                    string animationName = (node.Content as TreeItem)!.Text;
                    currentConfig = programConfig.GameThemeConfigs![gameThemeName].SubjectConfigs![subjectName].AnimationConfigs![animationName];
                    UpdateBreadcrumb(gameThemeName, subjectName, animationName);
                    var animationConfig = (currentConfig as AnimationConfig)!;
                    animationConfig.RecoverCroppedOffset ??= new RecoverCroppedOffset();
                    if (animationConfig.Offset == null)
                    {
                        animationConfig.Offset = new Vector2(0,0);
                    }
                    RegenerateCheckBox.IsChecked = animationConfig.Regenerate;
                    RecoverXCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.x;
                    RecoverYCheckBox.IsChecked = animationConfig.RecoverCroppedOffset.y;

                    DelayTextBox.Text = animationConfig.Delay.ToString();
                    OffsetXTextBox.Text = animationConfig.Offset.Value.X.ToString();
                    OffsetYTextBox.Text = animationConfig.Offset.Value.Y.ToString();

                    RegenerateCheckBox.Click += ClickRegenerateCheckBox;
                    RecoverXCheckBox.Click += ClickRecoverXCheckBox;
                    RecoverYCheckBox.Click += ClickRecoverYCheckBox;

                    DelayTextBox.ValueChanged += DelayTextBox_ValueChanged;
                    OffsetXTextBox.ValueChanged += OffsetXTextBox_ValueChanged;
                    OffsetYTextBox.ValueChanged += OffsetYTextBox_ValueChanged;


                    programConfig.SelectedNode = [(node.Parent.Parent.Content as TreeItem)!.Text, (node.Parent.Content as TreeItem)!.Text, (node.Content as TreeItem)!.Text];
                    break;
                default:
                    OpenSettings();
                    break;
            }

            SettingsToggleButton.IsChecked = false;
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
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.y = (sender as CheckBox)!.IsChecked!.Value;
        }

        private void ClickRecoverXCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.x = (sender as CheckBox)!.IsChecked!.Value;
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
            OpenSettings();
        }

        public void OpenSettings()
        {
            UpdateBreadcrumb("Settings & Help");

            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            GameThemePanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Visible;
            TreeViewControl.SelectedNode = null;
            SaveBarBorder.Visibility = Visibility.Collapsed;

            SettingsToggleButton.IsChecked = true;

            programConfig.SelectedNode = null;
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
            await Task.Delay(1);
            try
            {
                Processer.StartProcess();
                SetInfoBar(InfoBarSeverity.Success, "Successfully generated", $"Spritesheet generated into {programConfig.SelectedNode![1]}/generated");
            }
            catch (Exception er)
            {
                SetInfoBar(InfoBarSeverity.Error, "Generation failed", er.Message);
            }
        }
    }
}
