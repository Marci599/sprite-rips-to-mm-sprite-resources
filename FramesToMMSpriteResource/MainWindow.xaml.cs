using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Windows.UI;

namespace FramesToMMSpriteResource
{
    public class TreeItem
    {
        public string Text { get; set; }
        public string Path { get; set; }
        public string IconGlyph { get; set; }

        public TreeItem(string text, string iconGlyph)
        {
            Text = text;
            IconGlyph = iconGlyph;
        }
    }

    public class ProgramConfig
    {


        [JsonPropertyName("working_path")]
        public string? WorkingPath;

        [JsonPropertyName("reduce_file_size")]
        public bool ReduceFileSize = false;

        [JsonIgnore]
        public Dictionary<string, GameThemeConfig>? GameThemeConfigs = new Dictionary<string, GameThemeConfig>();

        public ProgramConfig() { }

        public ProgramConfig(string? workingPath = null, bool reduceFileSize = false, Dictionary<string, GameThemeConfig>? gameThemeConfigs = null)
        {
            WorkingPath = workingPath;
            ReduceFileSize = reduceFileSize;
            GameThemeConfigs = gameThemeConfigs;
        }
    }

    public abstract class ParentConfig
    {
        [JsonPropertyName("is_expanded")]
        public bool isExpanded = false;
    }

    public class GameThemeConfig : ParentConfig
    {
        [JsonPropertyName("is_hd")]
        public bool isHd = true;

        [JsonIgnore]
        public Dictionary<string, SubjectConfig>? SubjectConfigs = new Dictionary<string, SubjectConfig>();

        public GameThemeConfig() { }

        public GameThemeConfig(bool isHd, string? name, Dictionary<string, SubjectConfig>? subjectConfigs)
        {
            this.isHd = isHd;
            SubjectConfigs = subjectConfigs;
        }
    }

    public class SubjectConfig : ParentConfig
    {
        [JsonPropertyName("resize_to_percent")]
        public int resizeToPercent = 100;

        [JsonPropertyName("background_color")]
        public string? backgroundColor;

        [JsonPropertyName("color_threshold")]
        public int colorTreshold = 100;

        [JsonPropertyName("remove_background")]
        public bool removeBackground = true;

        [JsonPropertyName("crop_sprites")]
        public bool cropSprites = true;

        [JsonPropertyName("sheet")]
        public SheetConfig? Sheet;


        [JsonIgnore]
        public Dictionary<string, AnimationConfig>? AnimationConfigs = new Dictionary<string, AnimationConfig>();

        public SubjectConfig() { }

        public SubjectConfig(int resizeToPercent, string? backgroundColor, int colorTreshold, bool removeBackground, bool cropSprites, SheetConfig? sheet, string? name, Dictionary<string, AnimationConfig>? animationConfigs)
        {
            this.resizeToPercent = resizeToPercent;
            this.backgroundColor = backgroundColor;
            this.colorTreshold = colorTreshold;
            this.removeBackground = removeBackground;
            this.cropSprites = cropSprites;
            Sheet = sheet;
            AnimationConfigs = animationConfigs;
        }
    }

    public class SheetConfig
    {
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        public SheetConfig() { }

        public SheetConfig(int? width, int? height)
        {
            Width = width;
            Height = height;
        }
    }

    public class AnimationConfig
    {
        [JsonPropertyName("regenerate")]
        public bool Regenerate = true;

        [JsonPropertyName("delay")]
        public int Delay;

        [JsonPropertyName("offset")]
        public Vector2? Offset;

        [JsonPropertyName("recover_cropped_offset")]
        public RecoverCroppedOffset? RecoverCroppedOffset;

        public AnimationConfig() { }

        public AnimationConfig(bool regenerate, int delay, Vector2? offset, RecoverCroppedOffset? recoverCroppedOffset, string? name)
        {
            Regenerate = regenerate;
            Delay = delay;
            Offset = offset;
            RecoverCroppedOffset = recoverCroppedOffset;
        }
    }

    public class RecoverCroppedOffset
    {
        public bool x;
        public bool y;

        public RecoverCroppedOffset() { }
        public RecoverCroppedOffset(bool x, bool y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public sealed partial class MainWindow : Window
    {
        private const string CONFIG_FILENAME = "config.json";
        static string CONFIG_FAIL_TEXT = "Config failed to load";

        private bool _isSettingBackgroundColor;
        private string _lastValidBackgroundColor = "";

        private ProgramConfig? programConfig;
        private string workingPath;
        bool activated = false;
 

        public MainWindow()
        {
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 625));
            AppWindow.SetIcon("Assets/icon.ico");

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 750;
            presenter.PreferredMinimumHeight = 400;

            AppWindow.SetPresenter(presenter);


            programConfig = LoadConfig();


            SaveBarBorder.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, SaveBarBorderVisibilityChanged);

            SetUpTreeViewAndConfigs();

            Activated += MainWindow_Activated;

            
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

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (activated)
                {
                    Debug.WriteLine("ACTIVE WINDOW!");

                    ReloadTreeViewAndConfigs();
                }
                activated = true;
            }
            else
            {
                Debug.WriteLine("DISACTIVE WINDOW!");
                SaveAllConfigs();
            }
        }

        void SaveAllConfigs()
        {
            SaveJson(Path.Combine(workingPath, CONFIG_FILENAME), programConfig);

            var gameThemeDirs = Directory.GetDirectories(workingPath);

            foreach (var gameThemeDir in gameThemeDirs)
            {
                string gameThemeName = Path.GetFileName(gameThemeDir);

                var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);

                SaveJson(gameThemeConfigPath, programConfig.GameThemeConfigs[gameThemeName]);

                var subjectDirs = Directory.GetDirectories(gameThemeDir);
                foreach (var subjectDir in subjectDirs)
                {
                    string subjectName = Path.GetFileName(subjectDir);

                    var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);

                    SaveJson(subjectConfigPath, programConfig.GameThemeConfigs[gameThemeName].SubjectConfigs[subjectName]);

                    var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                    foreach (var animationDir in animationDirs)
                    {
                        string animationName = Path.GetFileName(animationDir);

                        var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);

                        SaveJson(animationConfigPath, programConfig.GameThemeConfigs[gameThemeName].SubjectConfigs[subjectName].AnimationConfigs[animationName]);
                    }
                }
            }          
        }

        JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            IncludeFields = true
        };

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

        private T LoadJson<T>(string filePath, string? errorTitle = null) where T : new()
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
          
                var title = errorTitle ?? "Load Failed";
                var filename = string.IsNullOrEmpty(filePath) ? "" : Path.GetFileName(filePath);
                SetInfoBar(InfoBarSeverity.Error, title, $"Could not load {filename}\n{ex.Message}");

                return new T();
            }
        }

        private ProgramConfig LoadConfig()
        {
            var exeDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
            var configPath = Path.Combine(exeDir, CONFIG_FILENAME);

            return LoadJson<ProgramConfig>(configPath, CONFIG_FAIL_TEXT);
        }

        void ReloadTreeViewAndConfigs()
        {
            programConfig = LoadConfig();
            programConfig.GameThemeConfigs = new Dictionary<string, GameThemeConfig>();
            TreeViewControl.RootNodes.Clear();
            if (!PrimaryInfoBar.IsClosable)
            {
                PrimaryInfoBar.IsOpen = false;
            }
            SetUpTreeViewAndConfigs();
        }

        void SetUpTreeViewAndConfigs()
        {
            if (TreeViewControl != null)
            {
                TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;
                TreeViewControl.Expanding += TreeViewControl_Expanding;
                TreeViewControl.Collapsed += TreeViewControl_Collapsed;

                // only populate once
                if (TreeViewControl.RootNodes.Count == 0)
                {

                    workingPath = programConfig?.WorkingPath;
                    if (string.IsNullOrWhiteSpace(workingPath))
                    {
                        workingPath = AppContext.BaseDirectory;
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

                    if (isUsingGameThemes())
                    {

                        AllowGeneration();
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            SetUpSubjectTreeViewAndConfigs(gameThemeDir, gameThemeName);
                        }
                    }
                    else
                    {
                        if (areSubjectsCorrect(workingPath))
                        {
                            AllowGeneration();

                            SetUpSubjectTreeViewAndConfigs(workingPath, "Game Theme");
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

        void SetUpSubjectTreeViewAndConfigs(string gameThemeDir, string gameThemeName)
        {
            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath, CONFIG_FAIL_TEXT);

            var gameThemeTreeItem = new TreeViewNode { Content = new TreeItem(gameThemeName, "\uE913"), IsExpanded = gameThemeConfig.isExpanded };

            var subjectDirs = Directory.GetDirectories(gameThemeDir);
            foreach (var subjectDir in subjectDirs)
            {
                string subjectName = Path.GetFileName(subjectDir);

                var subjectConfigPath = Path.Combine(subjectDir, CONFIG_FILENAME);
                SubjectConfig subjectConfig = LoadJson<SubjectConfig>(subjectConfigPath, CONFIG_FAIL_TEXT);

                var subjectTreeItem = new TreeViewNode { Content = new TreeItem(subjectName, "\uF158"), IsExpanded = subjectConfig.isExpanded };

                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                foreach (var animationDir in animationDirs)
                {
                    string animationName = Path.GetFileName(animationDir);

                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);
                    AnimationConfig animationConfig = LoadJson<AnimationConfig>(animationConfigPath, CONFIG_FAIL_TEXT);

                    subjectConfig.AnimationConfigs[animationName] = animationConfig;

                    var animationTreeItem = new TreeViewNode { Content = new TreeItem(animationName, "\uE805")};
                    subjectTreeItem.Children.Add(animationTreeItem);
                }

                gameThemeConfig.SubjectConfigs[subjectName] = subjectConfig;

                gameThemeTreeItem.Children.Add(subjectTreeItem);
            }

            programConfig.GameThemeConfigs[gameThemeName] = gameThemeConfig;

            TreeViewControl.RootNodes.Add(gameThemeTreeItem);
        }

        void UnallowGeneration()
        {
            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
            ReduceFileSizeCheckBox.IsEnabled = false;
            SaveAndGenerateButton.IsEnabled = false;
            ReduceFileSizeCheckBoxTexts.Opacity = 0.5;

        }

        void AllowGeneration()
        {
            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;
            ReduceFileSizeCheckBox.IsEnabled = true;
            SaveAndGenerateButton.IsEnabled = true;
            ReduceFileSizeCheckBoxTexts.Opacity = 1;
        }

        private bool isUsingGameThemes()
        {
            try
            {
                var firstLevelDirs = Directory.GetDirectories(workingPath);     

                foreach (var first in firstLevelDirs)
                {
                    if (!areSubjectsCorrect(first))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool areSubjectsCorrect(string path)
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
            TreeViewNode node = args.InvokedItem as TreeViewNode;

            int depth = GetNodeDepth(node);

            GameThemePanel.Visibility = Visibility.Collapsed;
            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Collapsed;


            switch (depth)
            {
                case 0: // root (game theme)
                    GameThemePanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Collapsed;
                    var gameThemeConfig = programConfig.GameThemeConfigs[(node.Content as TreeItem).Text];
                    IsHdCheckBox.IsChecked = gameThemeConfig.isHd;
                    break;
                case 1: // subject
                    SubjectPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    break;
                default: // depth >= 2 => animation
                    AnimationsPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    break;
            }

            SettingsToggleButton.IsChecked = false;
        }

        private void TreeViewControl_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;

            int depth = GetNodeDepth(node);

            switch (depth)
            {
                case 0: // root (game theme)
                    programConfig.GameThemeConfigs[(node.Content as TreeItem).Text].isExpanded = true;                 
                    break;
                case 1: // subject
                    programConfig.GameThemeConfigs[(node.Parent.Content as TreeItem).Text].SubjectConfigs[(node.Content as TreeItem).Text].isExpanded = true;
                    break;
                default: // depth >= 2 => animation
                    break;
            }
        }

        private void TreeViewControl_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            TreeViewNode node = args.Node;

            int depth = GetNodeDepth(node);

            switch (depth)
            {
                case 0: // root (game theme)
                    programConfig.GameThemeConfigs[(node.Content as TreeItem).Text].isExpanded = false;
                    break;
                case 1: // subject
                    programConfig.GameThemeConfigs[(node.Parent.Content as TreeItem).Text].SubjectConfigs[(node.Content as TreeItem).Text].isExpanded = false;
                    break;
                default: // depth >= 2 => animation
                    break;
            }
        }

        private int GetNodeDepth(TreeViewNode node)
        {
            int depth = -1;
            var n = node;
            while (n.Parent != null)
            {
                depth++;
                n = n.Parent;
            }
            return depth;
        }

        private void ClickSettings(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        public void OpenSettings()
        {
            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            GameThemePanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Visible;
            TreeViewControl.SelectedNode = null;
            SaveBarBorder.Visibility = Visibility.Collapsed;

            SettingsToggleButton.IsChecked = true;
        }

        private void SetInfoBar(InfoBarSeverity severity, string title, string message, bool isClosable = true)
        {
            PrimaryInfoBar.Title = title;
            PrimaryInfoBar.Message = message;
            PrimaryInfoBar.Severity = severity;
            PrimaryInfoBar.IsClosable = isClosable;
    
            SaveBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
  
            PrimaryInfoBar.IsOpen = true;
        }

        private void ClickPrimaryInfoBar(InfoBar sender, object args)
        {
            SaveBarBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
        }

        private void ClickGenerateHieararchy(object sender, RoutedEventArgs e)
        {

            var gameThemePath = Path.Combine(workingPath, "NameOfYourGameTheme1");
            var subject1Path = Path.Combine(gameThemePath, "NameOfYourSubject1", "raw");
            var subject2Path = Path.Combine(gameThemePath, "NameOfYourSubject2", "raw");
            Directory.CreateDirectory(Path.Combine(subject1Path, "NameOfYourAnim1"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "NameOfYourAnim2"));
            Directory.CreateDirectory(Path.Combine(subject1Path, "NameOfYourAnim3"));

            Directory.CreateDirectory(Path.Combine(subject2Path, "NameOfYourAnim1"));
            Directory.CreateDirectory(Path.Combine(subject2Path, "NameOfYourAnim2"));

            ReloadTreeViewAndConfigs();

        }
    }
}
