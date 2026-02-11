using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
using System.Threading.Tasks;
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

        [JsonPropertyName("selected_node")]
        public List<string>? SelectedNode;

        [JsonPropertyName("is_hd")]
        public bool isHd = true;

        [JsonIgnore]
        public Dictionary<string, GameThemeConfig>? GameThemeConfigs = new Dictionary<string, GameThemeConfig>();

        public ProgramConfig() { }

        public ProgramConfig(string? workingPath = null, bool reduceFileSize = false, Dictionary<string, GameThemeConfig>? gameThemeConfigs = null, List<string>? selectedNode = null)
        {
            WorkingPath = workingPath;
            ReduceFileSize = reduceFileSize;
            GameThemeConfigs = gameThemeConfigs;
            SelectedNode = selectedNode;
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

        public GameThemeConfig(bool isHd, bool isExpanded)
        {
            this.isHd = isHd;
            this.isExpanded = isExpanded;
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
        public SheetConfig Sheet = new SheetConfig();


        [JsonIgnore]
        public Dictionary<string, AnimationConfig>? AnimationConfigs = new Dictionary<string, AnimationConfig>();

        public SubjectConfig() { }

        public SubjectConfig(int resizeToPercent, string? backgroundColor, int colorTreshold, bool removeBackground, bool cropSprites, SheetConfig sheet, string? name, Dictionary<string, AnimationConfig>? animationConfigs)
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
        public int Delay = 1;

        [JsonPropertyName("offset")]
        public Vector2? Offset;

        [JsonPropertyName("recover_cropped_offset")]
        public RecoverCroppedOffset RecoverCroppedOffset = new RecoverCroppedOffset();

        public AnimationConfig() { }

        public AnimationConfig(bool regenerate, int delay, Vector2? offset, RecoverCroppedOffset recoverCroppedOffset, string? name)
        {
            Regenerate = regenerate;
            Delay = delay;
            Offset = offset;
            RecoverCroppedOffset = recoverCroppedOffset;
        }
    }

    public class RecoverCroppedOffset
    {
        public bool x = true;
        public bool y = true;

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

        bool usingGameThemes = false;
        bool hierarchyError = true;

        public ObservableCollection<string> BreadcrumbItems { get; } = new();
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



            HeaderBreadcrumbBar.ItemsSource = BreadcrumbItems;

            HeaderBreadcrumbBar.ItemClicked += BreadcrumbBar2_ItemClicked;

        }

        void UpdateBreadcrumb(params string[] items)
        {
            BreadcrumbItems.Clear();
            foreach (var item in items)
                BreadcrumbItems.Add(item);
        }

        private void BreadcrumbBar2_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            TreeViewControl.Focus(FocusState.Programmatic);

            string clickedItem = (string)args.Item;
            int clickedIndex = args.Index;

            Debug.WriteLine($"Breadcrumb clicked: {clickedItem} ({clickedIndex})");

            

            // Példa: vágjuk vissza a breadcrumbet a kattintott szintig
            while (programConfig.SelectedNode.Count > clickedIndex + 1)
            {
                programConfig.SelectedNode.RemoveAt(programConfig.SelectedNode.Count - 1);
            }

            foreach (TreeViewNode gameThemeNode in TreeViewControl.RootNodes)
            {
                if((gameThemeNode.Content as TreeItem).Text == programConfig.SelectedNode[0])
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
                            if (((subjectNode as TreeViewNode).Content as TreeItem).Text == programConfig.SelectedNode[1])
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
                                        if (((animationNode as TreeViewNode).Content as TreeItem).Text == programConfig.SelectedNode[2])
                                        {
                                       
                                            TreeViewControl.SelectedNode = animationNode;
                                            break;
                                        
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            WaitThenDisplayCorrectPanel(TreeViewControl.SelectedNode);
        }

        private void UpdateColorPreviewFromText(string? text)
        {
            if (TryNormalizeHexToColor(text, out string normalizedHex, out Windows.UI.Color color))
            {
                // Érvényes -> frissítjük a preview-t és mentjük az értéket a configba
                ColorPreviewBorder.Background = new SolidColorBrush(color);
                double alphaNormalized = color.A / 255.0;

                _lastValidBackgroundColor = normalizedHex;

                // ha currentConfig egy SubjectConfig, frissítjük azonnal a config mezõt
                if (currentConfig is SubjectConfig sc)
                {
                    sc.backgroundColor = normalizedHex;
                }

                // visszaállíthatunk vizuális hibaállapotot, ha korábban piros volt
                ColorPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
            }
            else
            {
                // Érvénytelen: áttetszõ elõnézet és hibaüzenet
                ColorPreviewBorder.Background = new SolidColorBrush();
        
                // opcionálisan piros keret:
                ColorPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
        }

        /// <summary>
        /// Normalize: elfogadja "#RRGGBB" vagy "#RRGGBBAA" (vagy ezek # nélkül), visszaadja a normalizált "#RRGGBB" vagy "#RRGGBBAA" formát és a Windows.UI.Color értéket.
        /// FONTOS: a 8 karakteres formátumnál az elrendezés RRGGBBAA (R,G,B az elején, alfa a végén).
        /// </summary>
        private bool TryNormalizeHexToColor(string? input, out string normalizedHex, out Windows.UI.Color color)
        {
            normalizedHex = string.Empty;
            color = new Windows.UI.Color();

            if (string.IsNullOrWhiteSpace(input))
                return true;

            string s = input.Trim();
            if (s.StartsWith("#"))
                s = s.Substring(1);

            // nagybetûs hex
            s = s.ToUpperInvariant();

            // Csak hex karaktereket engedjük
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"\A[0-9A-F]+\z"))
                return false;

            if (s.Length == 6)
            {
                // RRGGBB
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
                // RRGGBBAA (közismert HTML/CSS kiterjesztés): magasabb kompatibilitás miatt így olvassuk
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

            // Nem támogatott hossz (pl. 3 vagy 4 char rövidítést nem támogatunk itt)
            return false;
        }

        // Eseménykezelõ: TextChanged -> élõ frissítés
        private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSettingBackgroundColor) return;

            var tb = sender as TextBox;
            if (tb == null) return;

            var text = tb.Text;
            UpdateColorPreviewFromText(text);
        }

        // Ha szeretnéd explicit LostFocus-re is menteni a "utolsó érvényes" értéket,
        // fenntartok egy biztos mentést: ha a beírás érvénytelen, visszaállítja az utolsó jó értékre.
        private void ColorTextBox_LostFocus_ReturnToLastValid(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            if (!TryNormalizeHexToColor(tb.Text, out _, out _))
            {
                // Ha nincs semmi érvényes, töröljük a mezõt és a configot
                if (string.IsNullOrEmpty(_lastValidBackgroundColor))
                {
                    tb.Text = "";
                    if (currentConfig is SubjectConfig sc) sc.backgroundColor = null;
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
            else
            {
                // érvényes -> már mentettük TextChanged-ben
            }
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
                    programConfig = LoadConfig();
                    ReloadTreeViewAndConfigs();
                }
                activated = true;
                ReduceFileSizeCheckBox.IsChecked = programConfig.ReduceFileSize;
                WorkingPathTextBox.Text = programConfig.WorkingPath;
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

        private void WorkingPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            programConfig.WorkingPath = (sender as TextBox).Text.ToString();
            Debug.WriteLine("DEDITEEDD");
            ReloadTreeViewAndConfigs();
        }

        private void ReduceFileSizeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            programConfig.ReduceFileSize = (sender as CheckBox).IsChecked.Value;
        }


        async void WaitThenSave()
        {
            await Task.Delay(1);
            Debug.WriteLine("DISACTIVE WINDOW!");
            SaveAllConfigs();
        }

  
        void SaveAllConfigs()
        {

            if (hierarchyError)
            {
                SaveJson(Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME), programConfig);
                return;
            }

            if (usingGameThemes)
            {
                SaveJson(Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME), programConfig);
                var gameThemeDirs = Directory.GetDirectories(workingPath);

                foreach (var gameThemeDir in gameThemeDirs)
                {
                    string gameThemeName = Path.GetFileName(gameThemeDir);

                    var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);

                    SaveJson(gameThemeConfigPath, programConfig.GameThemeConfigs[gameThemeName]);

                    SaveSubjects(gameThemeDir, gameThemeName);
                }
            }
            else
            {
                programConfig.isHd = programConfig.GameThemeConfigs["Game Theme"].isHd;
                SaveJson(Path.Combine(AppContext.BaseDirectory, CONFIG_FILENAME), programConfig);
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
            hierarchyError = true;
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
                    usingGameThemes = isUsingGameThemes();
                    if (usingGameThemes)
                    {
                        hierarchyError = false;
                        AllowGeneration();
                        var gameThemeDirs = Directory.GetDirectories(workingPath);

                        foreach (var gameThemeDir in gameThemeDirs)
                        {
                            string gameThemeName = Path.GetFileName(gameThemeDir);

                            var gameThemeConfigPath = Path.Combine(gameThemeDir, CONFIG_FILENAME);
                            GameThemeConfig gameThemeConfig = LoadJson<GameThemeConfig>(gameThemeConfigPath, CONFIG_FAIL_TEXT);

                 

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
                        if (areSubjectsCorrect(workingPath))
                        {
                            hierarchyError = false;
                            AllowGeneration();

                            GameThemeConfig gameThemeConfig = new GameThemeConfig(programConfig.isHd, true);

                      

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

                    if (programConfig.SelectedNode != null &&
                        programConfig.SelectedNode.Count == 3 &&
                        programConfig.SelectedNode[0] == gameThemeName &&
                        programConfig.SelectedNode[1] == subjectName &&
                        programConfig.SelectedNode[2] == animationName)
                    {
                        TreeViewControl.SelectedNode = animationTreeItem;
                    }
                }

                gameThemeConfig.SubjectConfigs[subjectName] = subjectConfig;

                gameThemeTreeItem.Children.Add(subjectTreeItem);

                if (programConfig.SelectedNode != null &&
                    programConfig.SelectedNode.Count == 2 &&
                    programConfig.SelectedNode[0] == gameThemeName &&
                    programConfig.SelectedNode[1] == subjectName)
                {
                    TreeViewControl.SelectedNode = subjectTreeItem;
                }
            }

            programConfig.GameThemeConfigs[gameThemeName] = gameThemeConfig;

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

        object currentConfig;

        private void TreeViewControl_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            WaitThenDisplayCorrectPanel(args.InvokedItem as TreeViewNode);
        }

        async void WaitThenDisplayCorrectPanel(TreeViewNode node)
        {
            await Task.Delay(1);
            DisplayCorrectPanel(node);
        }

        void DisplayCorrectPanel(TreeViewNode node)
        {


            int depth = GetNodeDepth(node);

            GameThemePanel.Visibility = Visibility.Collapsed;
            SubjectPanel.Visibility = Visibility.Collapsed;
            AnimationsPanel.Visibility = Visibility.Collapsed;
            HelpPanel.Visibility = Visibility.Collapsed;

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

            string gameThemeName;
            string subjectName;
            switch (depth)
            {
                case 0: // root (game theme)
                    GameThemePanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Collapsed;
                    gameThemeName = (node.Content as TreeItem).Text;
                    currentConfig = programConfig.GameThemeConfigs[gameThemeName];
                    UpdateBreadcrumb(gameThemeName);
                    var gameThemeConfig = (currentConfig as GameThemeConfig);
                    IsHdCheckBox.IsChecked = gameThemeConfig!.isHd;
                    IsHdCheckBox.Click += ClickIsHdCheckBox;

                    programConfig.SelectedNode = [(node.Content as TreeItem).Text];
                    break;
                case 1: // subject
                    SubjectPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    gameThemeName = (node.Parent.Content as TreeItem).Text;
                    subjectName = (node.Content as TreeItem).Text;
                    currentConfig = programConfig.GameThemeConfigs[gameThemeName].SubjectConfigs[subjectName];
                    UpdateBreadcrumb(gameThemeName, subjectName);
                    var subjectConfig = (currentConfig as SubjectConfig);
                    if (subjectConfig.Sheet == null)
                    {
                        subjectConfig.Sheet = new SheetConfig();
                    }

                    RemoveBackgroundCheckBox.IsChecked = subjectConfig.removeBackground;
                    CropSpritesCheckBox.IsChecked = subjectConfig.cropSprites;

                    ResizeTextBox.Text = subjectConfig.resizeToPercent.ToString();
                    ColorTextBox.Text = subjectConfig.backgroundColor;
                    ThresholdTextBox.Text = subjectConfig.colorTreshold.ToString();
                    SheetWidthTextBox.Text = subjectConfig.Sheet.Width.ToString();
                    SheetHeightTextBox.Text = subjectConfig.Sheet.Height.ToString();

                    _isSettingBackgroundColor = true;
                    ColorTextBox.Text = subjectConfig.backgroundColor ?? "";
                    UpdateColorPreviewFromText(subjectConfig.backgroundColor);
                    _isSettingBackgroundColor = false;

                    RemoveBackgroundCheckBox.Click += ClickRemoveBackground;
                    CropSpritesCheckBox.Click += ClickCropSpritesCheckBox;

                    ResizeTextBox.ValueChanged += ResizeTextBox_ValueChanged;
                    ColorTextBox.TextChanged += ColorTextBox_TextChanged;
                    ColorTextBox.LostFocus += ColorTextBox_LostFocus_ReturnToLastValid;
                    ThresholdTextBox.ValueChanged += ThresholdTextBox_ValueChanged;

                    SheetWidthTextBox.ValueChanged += SheetWidthTextBox_ValueChanged;
                    SheetHeightTextBox.ValueChanged += SheetHeightTextBox_ValueChanged;

                    programConfig.SelectedNode = [(node.Parent.Content as TreeItem).Text, (node.Content as TreeItem).Text];
                    break;
                case 2: // depth >= 2 => animation
                    AnimationsPanel.Visibility = Visibility.Visible;
                    SaveBarBorder.Visibility = Visibility.Visible;
                    gameThemeName = (node.Parent.Parent.Content as TreeItem).Text;
                    subjectName = (node.Parent.Content as TreeItem).Text;
                    string animationName = (node.Content as TreeItem).Text;
                    currentConfig = programConfig.GameThemeConfigs[gameThemeName].SubjectConfigs[subjectName].AnimationConfigs[animationName];
                    UpdateBreadcrumb(gameThemeName, subjectName, animationName);
                    var animationConfig = (currentConfig as AnimationConfig);
                    if (animationConfig.RecoverCroppedOffset == null)
                    {
                        animationConfig.RecoverCroppedOffset = new RecoverCroppedOffset();
                    }
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


                    programConfig.SelectedNode = [(node.Parent.Parent.Content as TreeItem).Text, (node.Parent.Content as TreeItem).Text, (node.Content as TreeItem).Text];
                    break;
                default:
                    OpenSettings();
                    break;
            }

            SettingsToggleButton.IsChecked = false;
        }

        private void OffsetYTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as AnimationConfig)!.Offset = new Vector2((currentConfig as AnimationConfig)!.Offset.Value.X, (float)sender.Value);
        }

        private void OffsetXTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as AnimationConfig)!.Offset = new Vector2((float)sender.Value, (currentConfig as AnimationConfig)!.Offset.Value.Y);
        }

        private void DelayTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as AnimationConfig)!.Delay = (int)sender.Value;
        }

        private void SheetHeightTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.Sheet.Height = (int)sender.Value;
        }

        private void SheetWidthTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.Sheet.Width = (int)sender.Value;
        }

        private void ThresholdTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.colorTreshold = (int)sender.Value;
        }

        private void ColorTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            (currentConfig as SubjectConfig)!.backgroundColor = (sender as TextBox).Text.ToString();
        }

        private void ResizeTextBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            (currentConfig as SubjectConfig)!.resizeToPercent = (int)sender.Value;
        }

        private void ClickRecoverYCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.y = (sender as CheckBox).IsChecked!.Value;
        }

        private void ClickRecoverXCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.RecoverCroppedOffset.x = (sender as CheckBox).IsChecked!.Value;
        }

        private void ClickRegenerateCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as AnimationConfig)!.Regenerate = (sender as CheckBox).IsChecked!.Value;
        }

        private void ClickCropSpritesCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as SubjectConfig)!.cropSprites = (sender as CheckBox).IsChecked!.Value;
        }

        private void ClickRemoveBackground(object sender, RoutedEventArgs e)
        {
            (currentConfig as SubjectConfig)!.removeBackground = (sender as CheckBox).IsChecked!.Value;
        }

        private void ClickIsHdCheckBox(object sender, RoutedEventArgs e)
        {
            (currentConfig as GameThemeConfig)!.isHd = (sender as CheckBox).IsChecked!.Value;
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
