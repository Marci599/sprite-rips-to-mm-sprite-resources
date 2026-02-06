using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Numerics;
using System.Diagnostics;

namespace FramesToMMSpriteResource
{
    public class TreeItem
    {
        public string Text { get; set; }
        public string Path { get; set; }
        public string IconGlyph { get; set; }

        public TreeItem(string text, string iconGlyph, string path)
        {
            Text = text;
            Path = path;
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

    public class GameThemeConfig
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

    public class SubjectConfig
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

    

            programConfig = LoadConfig();
            SetUpTreeViewAndConfigs();

            this.Activated += MainWindow_Activated;


        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // A WindowActivationState jelzi, hogy mi történt az ablakkal
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (activated)
                {
                    Debug.WriteLine("ACTIVE WINDOW!");

                    ReloadTreeViewAndConfigs();
                }
                activated = true;

            }
        }


        private T LoadJson<T>(string filePath, string? errorTitle = null) where T : new()
        {
            try
            {
                if (!File.Exists(filePath))
                    return new T();

                var json = File.ReadAllText(filePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    IncludeFields = true
                };

                var obj = JsonSerializer.Deserialize<T>(json, options);
     
    
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

            // Delegates all loading and error handling to LoadJson<T>.
            return LoadJson<ProgramConfig>(configPath, CONFIG_FAIL_TEXT);
        }

        private void UpdateColorPreview(string? colorValue = null)
        {
            if (ColorPreviewBorder == null)
                return;

            try
            {
                if (string.IsNullOrEmpty(colorValue))
                {
                    ColorPreviewBorder.Background = new SolidColorBrush(Colors.White);
                }
                else
                {
                    var normalized = NormalizeBackgroundColor(colorValue);
                    if (normalized.Length >= 7)
                    {
                        var hexColor = normalized.Substring(0, 7);
                        var color = HexToColor(hexColor);
                        ColorPreviewBorder.Background = new SolidColorBrush(color);
                    }
                }
            }
            catch
            {
                ColorPreviewBorder.Background = new SolidColorBrush(Colors.White);
            }
        }

        private string NormalizeBackgroundColor(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var text = value.Trim();
            if (text.StartsWith("#"))
                text = text.Substring(1);

            text = text.ToUpper();

            if (text.Length == 3 || text.Length == 4)
                text = string.Concat(text.Select(ch => $"{ch}{ch}"));

            if (text.Length != 6 && text.Length != 8)
                throw new ArgumentException("Invalid color format");

            var validChars = new HashSet<char>("0123456789ABCDEF");
            if (text.Any(ch => !validChars.Contains(ch)))
                throw new ArgumentException("Invalid hex characters");

            return "#" + text;
        }

        private Color HexToColor(string hex)
        {
            hex = hex.TrimStart('#');
            var r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            return Color.FromArgb(255, r, g, b);
        }

        void ReloadTreeViewAndConfigs()
        {
            programConfig = LoadConfig();
            programConfig.GameThemeConfigs = new Dictionary<string, GameThemeConfig>();
            TreeViewControl.RootNodes.Clear();
            SetUpTreeViewAndConfigs();
        }

        void SetUpTreeViewAndConfigs()
        {
            if (TreeViewControl != null)
            {
                TreeViewControl.ItemInvoked += TreeViewControl_ItemInvoked;

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
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                        TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                        TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                        ReduceFileSizeCheckBox.IsEnabled = false;
                        SaveAndGenerateButton.IsEnabled = false;
                        ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
                        return;
                    }

                    if (Directory.GetDirectories(workingPath).Length == 0)
                    {
                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                        TreeViewPlaceHolderText.Text = "Empty working directory";
                        TreeViewPlaceHolderButton.Visibility = Visibility.Visible;
                        ReduceFileSizeCheckBox.IsEnabled = false;
                        SaveAndGenerateButton.IsEnabled = false;
                        ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
                        OpenSettings();
                        return;
                    }

                    if (isUsingGameThemes())
                    {

                        TreeViewPlaceHolderStackPanel.Visibility = Visibility.Collapsed;
                        ReduceFileSizeCheckBox.IsEnabled = true;
                        SaveAndGenerateButton.IsEnabled = true;
                        ReduceFileSizeCheckBoxTexts.Opacity = 1;
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath, CONFIG_FAIL_TEXT);

                            var gameThemeTreeItem = new TreeViewNode { Content = new TreeItem(gameThemeName, "\uE913", "") };

                            var subjectDirs = Directory.GetDirectories(gameThemeDir);
                            foreach (var subjectDir in subjectDirs)
                            {
                                string subjectName = Path.GetFileName(subjectDir);

                                var subjectConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                                SubjectConfig subjectConfig = LoadJson<SubjectConfig>(subjectConfigPath, CONFIG_FAIL_TEXT);

                                var subjectTreeItem = new TreeViewNode { Content = new TreeItem(subjectName, "\uF158", "") };

                                var animationDirs = Directory.GetDirectories(Path.Combine(subjectDir, "raw"));
                                foreach (var animationDir in animationDirs)
                                {
                                    string animationName = Path.GetFileName(animationDir);

                                    var animationConfigPath = Path.Combine(animationDir, CONFIG_FILENAME);
                                    AnimationConfig animationConfig = LoadJson<AnimationConfig>(animationConfigPath, CONFIG_FAIL_TEXT);

                                    subjectConfig.AnimationConfigs[animationName] = animationConfig;

                                    var animationTreeItem = new TreeViewNode { Content = new TreeItem(animationName, "\uE805", "") };
                                    subjectTreeItem.Children.Add(animationTreeItem);
                                }

                                gameThemeConfig.SubjectConfigs[subjectName] = subjectConfig;

                                gameThemeTreeItem.Children.Add(subjectTreeItem);
                            }

                            programConfig.GameThemeConfigs[gameThemeName] = gameThemeConfig;

                            TreeViewControl.RootNodes.Add(gameThemeTreeItem);
                        }
                    }
                    else
                    {
                        if (areSubjectsCorrect())
                        {

                        }
                        else
                        {
                            SetInfoBar(InfoBarSeverity.Error, "Wrong hierarchy or missing folders", "The way you've set your files and folders up is wrong...", false);
                            TreeViewPlaceHolderStackPanel.Visibility = Visibility.Visible;
                            TreeViewPlaceHolderButton.Visibility = Visibility.Collapsed;
                            TreeViewPlaceHolderText.Text = "Cannot display hierarchy";
                            ReduceFileSizeCheckBox.IsEnabled = false;
                            SaveAndGenerateButton.IsEnabled = false;
                            ReduceFileSizeCheckBoxTexts.Opacity = 0.5;
                            OpenSettings();
                        }
                    }
                }
            }
        }

        private bool isUsingGameThemes()
        {
            try
            {
                var firstLevelDirs = Directory.GetDirectories(workingPath);     

                foreach (var first in firstLevelDirs)
                {
                    // Second-level directories under each first-level dir
                    var secondLevelDirs = Directory.GetDirectories(first);
                    if (secondLevelDirs.Length == 0)
                    {
                        return false;
                    }

                    foreach (var second in secondLevelDirs)
                    {
                        var rawPath = Path.Combine(second, "raw");
                        if (!Directory.Exists(rawPath))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool areSubjectsCorrect()
        {
            try
            {
                var firstLevelDirs = Directory.GetDirectories(workingPath);

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
            // args.InvokedItem is the Content object we set (a FrameworkElement)
            var invokedElement = args.InvokedItem as FrameworkElement;
            var key = invokedElement?.Tag as string;

            if (string.IsNullOrEmpty(key))
            {
                // fallback if something else (strings)
                key = (args.InvokedItem as string) ?? "";
            }

            switch (key.ToLowerInvariant())
            {
                case "subject":
                    SubjectPanel.Visibility = Visibility.Visible;
                    AnimationsPanel.Visibility = Visibility.Collapsed;
                    HelpPanel.Visibility = Visibility.Collapsed;
                    break;
                case "animations":
                case "walk":
                case "idle":
                    SubjectPanel.Visibility = Visibility.Collapsed;
                    AnimationsPanel.Visibility = Visibility.Visible;
                    HelpPanel.Visibility = Visibility.Collapsed;
                    break;
                default:
                    SubjectPanel.Visibility = Visibility.Visible;
                    AnimationsPanel.Visibility = Visibility.Collapsed;
                    HelpPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ClickSettings(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        public void OpenSettings()
        {
            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Visible;
            TreeViewControl.SelectedNode = null;
        }

        private void ValidateNumericInput(TextBox textBox)
        {
            var text = textBox.Text;
            if (string.IsNullOrEmpty(text))
                return;

            var filtered = new string(text.Where(char.IsDigit).ToArray());
            if (filtered != text)
                textBox.Text = filtered;
        }

        private void ValidateSignedNumericInput(TextBox textBox)
        {
            var text = textBox.Text;
            if (string.IsNullOrEmpty(text) || text == "-")
                return;

            if (text.StartsWith("-"))
            {
                var rest = new string(text.Substring(1).Where(char.IsDigit).ToArray());
                textBox.Text = "-" + rest;
            }
            else
            {
                var filtered = new string(text.Where(char.IsDigit).ToArray());
                if (filtered != text)
                    textBox.Text = filtered;
            }
        }

        private void ColorTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var text = ColorTextBox!.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _lastValidBackgroundColor = "";
            }
            else
            {
                try
                {
                    _lastValidBackgroundColor = NormalizeBackgroundColor(text);
                }
                catch
                {
                    _lastValidBackgroundColor = _lastValidBackgroundColor ?? "";
                }
            }

            _isSettingBackgroundColor = true;
            ColorTextBox.Text = _lastValidBackgroundColor;
            _isSettingBackgroundColor = false;
            UpdateColorPreview(_lastValidBackgroundColor);
        }

        private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSettingBackgroundColor)
                return;

            var value = ColorTextBox!.Text.Trim();
            if (string.IsNullOrEmpty(value))
            {
                UpdateColorPreview("");
            }
            else
            {
                try
                {
                    var normalized = NormalizeBackgroundColor(value);
                    UpdateColorPreview(normalized);
                }
                catch
                {
                    UpdateColorPreview("");
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveAll())
            {
                SetInfoBar(InfoBarSeverity.Success, "Saved", "Configuration files have been saved.");
            }
        }

        

        private bool SaveAll()
        {
            try
            {


                return true;
            }
            catch (Exception ex)
            {
                SetInfoBar(InfoBarSeverity.Error, "Save Failed", $"Could not save configuration files\n{ex.Message}");
                return false;
            }
        }

        private void SetInfoBar(InfoBarSeverity severity, string title, string message, bool isClosable = true)
        {
            PrimaryInfoBar.Title = title;
            PrimaryInfoBar.Message = message;
            PrimaryInfoBar.Severity = severity;
            PrimaryInfoBar.IsClosable = isClosable;
            SaveBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
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
