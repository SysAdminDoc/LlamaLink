#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LlamaLink;

public partial class MainWindow : Window
{
    // ── State ────────────────────────────────────────────────────────────
    private readonly ObservableCollection<ChatMessageVM> _chatMessages = new();
    private readonly List<Dictionary<string, string>> _messages = new();
    private readonly Dictionary<int, IReadOnlyList<ChatImageAttachment>> _messageImages = new();
    private readonly List<ChatImageAttachment> _pendingImages = new();
    private readonly ObservableCollection<string> _tokenProbabilityRows = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _streamTimer;
    private readonly DispatcherTimer _healthTimer;
    private readonly string _settingsPath;
    private readonly string _chatHistoryDir;
    private readonly string _promptLibraryPath;
    private readonly string _ragIndexPath;
    private RagIndex _ragIndex = new();
    private List<RagSearchResult> _lastRagMatches = new();
    private bool _ragIndexing;
    private readonly DispatcherTimer _ragSyncTimer;
    private readonly HashSet<string> _ragPendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _ragWatcher;
    private string? _ragWatchedFolder;
    private bool _ragWatchOnStartup;
    private CancellationTokenSource? _serverUpdateCts;
    private LlamaServerRelease? _latestServerRelease;

    private Process? _serverProcess;
    private Process? _speechRecorder;
    private string? _speechAudioPath;
    private CancellationTokenSource? _speechCts;
    private CancellationTokenSource? _imageGenCts;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _downloadCts;
    private bool _streaming;
    private string _streamBuffer = "";
    private bool _streamDirty;
    private int _tokenCount;
    private long _streamStartTime;
    private TokenProbabilityOptions _activeTokenProbabilityOptions = new(false, 5);
    private bool _activeTokenProbabilityBackendSupports;
    private ToolCallAccumulator? _toolCallAccumulator;
    private readonly Queue<ToolCallRequest> _pendingToolCalls = new();
    private readonly List<SystemPromptEntry> _promptEntries = new();
    private bool _updatingPrompts;
    private bool _updatingFewShot;
    private string? _currentChatFile;
    private string? _chatAttachedContext;
    private string? _branchId;
    private string? _parentChat;
    private int? _branchPoint;
    private string? _branchName;
    private Dictionary<string, string>? _regeneratingOriginal;
    private bool _serverManaged;
    private string? _hfSelectedRepo;
    private List<HfModelResult> _hfCachedResults = new();
    private CancellationTokenSource? _hfCardCts;
    private Thread? _serverThread;
    private readonly List<ServerProfile> _serverProfiles = new();
    private bool _updatingProfiles;
    private PromptInspection? _lastPromptInspection;

    // ── Brushes for chat bubbles ─────────────────────────────────────────
    private static readonly SolidColorBrush UserAccent = new(Color.FromRgb(0x89, 0xB4, 0xFA));
    private static readonly SolidColorBrush UserBg = new(Color.FromRgb(0x31, 0x32, 0x44));
    private static readonly SolidColorBrush AssistantAccent = new(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly SolidColorBrush AssistantBg = new(Color.FromRgb(0x18, 0x18, 0x25));
    private static readonly SolidColorBrush SystemAccent = new(Color.FromRgb(0xCB, 0xA6, 0xF7));
    private static readonly SolidColorBrush SystemBg = new(Color.FromRgb(0x11, 0x11, 0x1B));

    public MainWindow()
    {
        InitializeComponent();
        TokenProbabilityList.ItemsSource = _tokenProbabilityRows;

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "settings.json");
        _chatHistoryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "chats");
        _promptLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "prompts.json");
        _ragIndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "rag-index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        Directory.CreateDirectory(_chatHistoryDir);
        try
        {
            _ragIndex = RagIndexStore.Load(_ragIndexPath);
        }
        catch
        {
            _ragIndex = new RagIndex();
        }

        ChatMessages.ItemsSource = _chatMessages;
        RefreshImageAttachments();

        _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _streamTimer.Tick += (_, _) => FlushStream();

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _healthTimer.Tick += async (_, _) => await CheckServerHealth();

        _ragSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _ragSyncTimer.Tick += async (_, _) =>
        {
            _ragSyncTimer.Stop();
            await SyncWatchedRagFolderAsync();
        };

        LoadSettings();
        LoadPromptLibrary();
        RefreshChatHistory();
        RefreshForkMessageOptions();
        RefreshRagSources();
        if (_ragWatchOnStartup && Directory.Exists(RagFolderBox.Text.Trim()))
            StartRagFolderWatch(RagFolderBox.Text.Trim());
        UpdateChatContextLabel();

        Closing += OnWindowClosing;
    }

    // ── Data models ──────────────────────────────────────────────────────
    public class ChatMessageVM
    {
        public string RoleLabel { get; set; } = "";
        public string Content { get; set; } = "";
        public SolidColorBrush Accent { get; set; } = UserAccent;
        public SolidColorBrush Background { get; set; } = UserBg;
    }

    public class BranchMessageOption
    {
        public int Index { get; set; }
        public string Content { get; set; } = "";
    }

    public class HfModelResult
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public int Downloads { get; set; }
        public int Likes { get; set; }
        public string DownloadsDisplay => Downloads.ToString("N0");
        public string LikesDisplay => Likes.ToString("N0");
    }

    public class HfFileResult
    {
        public string Filename { get; set; } = "";
        public string Quant { get; set; } = "";
        public long Size { get; set; }
        public string SizeDisplay => Size > 0
            ? Size >= 1024L * 1024 * 1024
                ? $"{Size / (1024.0 * 1024 * 1024):F2} GB"
                : Size >= 1024 * 1024
                    ? $"{Size / (1024.0 * 1024):F1} MB"
                    : $"{Size / 1024.0:F0} KB"
            : "?";
    }

    // ── System prompt library ───────────────────────────────────────────
    private void LoadPromptLibrary()
    {
        try
        {
            _promptEntries.Clear();
            _promptEntries.AddRange(PromptLibraryStore.Load(_promptLibraryPath));
        }
        catch
        {
            _promptEntries.Clear();
            _promptEntries.AddRange(PromptLibraryStore.CreateDefaults());
            PromptStatusLabel.Text = "Prompt file could not be read; using built-in prompts.";
        }

        RefreshPromptDomains("Code");
    }

    private void RefreshPromptDomains(string? preferredDomain = null)
    {
        _updatingPrompts = true;
        try
        {
            var existing = PromptDomainCombo.Items
                .OfType<ComboBoxItem>()
                .Select(item => item.Tag?.ToString() ?? "")
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var domain in _promptEntries
                .Select(entry => entry.Domain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!existing.Contains(domain))
                {
                    PromptDomainCombo.Items.Add(new ComboBoxItem { Content = domain, Tag = domain });
                    existing.Add(domain);
                }
            }

            var target = preferredDomain ?? (PromptDomainCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Code";
            PromptDomainCombo.SelectedItem = PromptDomainCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
                ?? PromptDomainCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _updatingPrompts = false;
        }

        RefreshPromptList();
    }

    private void RefreshPromptList(string? selectedId = null)
    {
        var domain = (PromptDomainCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Code";
        var visible = _promptEntries
            .Where(entry => string.Equals(entry.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _updatingPrompts = true;
        try
        {
            PromptCombo.ItemsSource = null;
            PromptCombo.ItemsSource = visible;
            if (!string.IsNullOrEmpty(selectedId))
            {
                PromptCombo.SelectedItem = visible.FirstOrDefault(entry =>
                    string.Equals(entry.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                PromptCombo.SelectedIndex = visible.Count > 0 ? 0 : -1;
            }
        }
        finally
        {
            _updatingPrompts = false;
        }

        if (PromptCombo.SelectedItem is SystemPromptEntry selected)
            LoadPromptIntoEditor(selected);
        else
        {
            PromptNameBox.Text = "";
            PromptEditorBox.Text = "";
        }
    }

    private void PromptDomain_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingPrompts) RefreshPromptList();
    }

    private void Prompt_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPrompts || PromptCombo.SelectedItem is not SystemPromptEntry entry) return;
        LoadPromptIntoEditor(entry);
    }

    private void LoadPromptIntoEditor(SystemPromptEntry entry)
    {
        PromptNameBox.Text = entry.Name;
        PromptEditorBox.Text = entry.Content;
        PromptStatusLabel.Text = entry.BuiltIn
            ? $"Built-in {entry.Domain} prompt. Save custom to create an editable copy."
            : $"Custom {entry.Domain} prompt.";
    }

    private void ApplyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptEditorBox.Text))
        {
            PromptStatusLabel.Text = "Choose or write a prompt before applying it.";
            return;
        }

        SystemPromptBox.Text = PromptEditorBox.Text.Trim();
        PromptStatusLabel.Text = "Prompt applied to the active chat settings.";
        StatusLabel.Text = "System prompt applied";
    }

    private void SavePrompt_Click(object sender, RoutedEventArgs e)
    {
        var domain = (PromptDomainCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Custom";
        var name = PromptNameBox.Text.Trim();
        var content = PromptEditorBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(content))
        {
            PromptStatusLabel.Text = "Custom prompts require a name and content.";
            return;
        }

        var selected = PromptCombo.SelectedItem as SystemPromptEntry;
        var entry = new SystemPromptEntry
        {
            Id = selected is not null && !selected.BuiltIn ? selected.Id : Guid.NewGuid().ToString("N"),
            Domain = domain,
            Name = name,
            Content = content,
            BuiltIn = false,
        };
        PromptLibraryStore.Upsert(_promptEntries, entry);
        PromptLibraryStore.Save(_promptLibraryPath, _promptEntries);
        RefreshPromptDomains(domain);
        RefreshPromptList(entry.Id);
        PromptStatusLabel.Text = $"Saved custom prompt '{entry.Name}'.";
    }

    private void DeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (PromptCombo.SelectedItem is not SystemPromptEntry entry)
        {
            PromptStatusLabel.Text = "Select a prompt first.";
            return;
        }
        if (entry.BuiltIn)
        {
            PromptStatusLabel.Text = "Built-in prompts cannot be deleted.";
            return;
        }

        _promptEntries.Remove(entry);
        PromptLibraryStore.Save(_promptLibraryPath, _promptEntries);
        RefreshPromptList();
        PromptStatusLabel.Text = $"Deleted prompt '{entry.Name}'.";
    }

    private void ImportPrompts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import system prompts",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = PromptLibraryStore.Parse(File.ReadAllText(dialog.FileName));
            foreach (var entry in imported)
            {
                entry.BuiltIn = false;
                PromptLibraryStore.Upsert(_promptEntries, entry);
            }
            PromptLibraryStore.Save(_promptLibraryPath, _promptEntries);
            RefreshPromptDomains(imported.FirstOrDefault()?.Domain ?? "Custom");
            PromptStatusLabel.Text = $"Imported {imported.Count} prompt(s).";
        }
        catch (Exception ex)
        {
            PromptStatusLabel.Text = $"Prompt import failed: {ex.Message}";
        }
    }

    private void ExportPrompts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export system prompts",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "llamalink-prompts.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, PromptLibraryStore.Serialize(_promptEntries));
            PromptStatusLabel.Text = $"Exported prompts to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            PromptStatusLabel.Text = $"Prompt export failed: {ex.Message}";
        }
    }

    // ── Mode toggle ──────────────────────────────────────────────────────
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarCol.Width.Value > 0)
        {
            SidebarCol.MinWidth = 0;
            SidebarCol.Width = new GridLength(0);
        }
        else
        {
            SidebarCol.MinWidth = 280;
            SidebarCol.Width = new GridLength(340);
        }
    }

    private void ManagedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (ExeRow is null) return;

        bool managed = ManagedCheck.IsChecked == true;
        ExeRow.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ExtUrlRow.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        ExtBackendRow.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        ExtModelRow.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        PortRow.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        StartBtn.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ConnectBtn.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        ModelGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ServerParamsGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ProfileGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ServerUpdateGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Backend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BackendCombo.SelectedItem is not ComboBoxItem item) return;
        if (BackendAdapter.Parse(item.Tag?.ToString() ?? "") == LlamaBackendKind.Ollama
            && string.IsNullOrWhiteSpace(ExtModelBox.Text))
        {
            ExtModelBox.Text = "llama3.2";
        }
    }

    // ── Browse dialogs ───────────────────────────────────────────────────
    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select llama-server executable",
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            FileName = ExePathBox.Text
        };
        if (dlg.ShowDialog() == true)
            ExePathBox.Text = dlg.FileName;
    }

    private void BrowseModelFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Model Folder",
            InitialDirectory = ModelFolderBox.Text
        };
        if (dlg.ShowDialog() == true)
            ModelFolderBox.Text = dlg.FolderName;
    }

    // ── Model scanning ───────────────────────────────────────────────────
    private void ModelFolder_Changed(object sender, TextChangedEventArgs e)
    {
        RefreshModels(ModelFolderBox.Text);
    }

    private void RefreshModels(string folder)
    {
        var selectedPath = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        ModelCombo.Items.Clear();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;

        var models = ScanModels(folder);
        foreach (var model in models)
        {
            ModelCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{model.name}  ({model.sizeGb:F1} GB)",
                Tag = model.path
            });
        }

        if (!string.IsNullOrEmpty(selectedPath))
        {
            ModelCombo.SelectedItem = ModelCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(), selectedPath, StringComparison.OrdinalIgnoreCase));
        }
        if (models.Count > 0)
            StatusLabel.Text = $"Found {models.Count} model(s)";
    }

    private static List<(string name, string path, double sizeGb)> ScanModels(string folder)
    {
        var models = new List<(string name, string path, double sizeGb)>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*.gguf", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(f);
                    models.Add((info.Name, f, info.Length / (1024.0 * 1024 * 1024)));
                }
                catch { }
            }
        }
        catch { }
        models.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return models;
    }

    private void ModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModelCombo.SelectedItem is ComboBoxItem item && item.Tag is string path && File.Exists(path))
        {
            var info = new FileInfo(path);
            ModelInfoLabel.Text = $"{info.Name} - {info.Length / (1024.0 * 1024 * 1024):F2} GB";
            QuantRecommendationLabel.Text = "Select Recommend to compare local quant variants.";
        }
        else
        {
            ModelInfoLabel.Text = "";
            QuantRecommendationLabel.Text = "Select a model to compare its local quant variants.";
        }
    }

    private void ProfileCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfiles || ProfileCombo.SelectedItem is not ServerProfile profile)
            return;

        ApplyServerProfile(profile);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ProfileStatusLabel.Text = "Enter a profile name first.";
            return;
        }

        if (!TryParsePositiveInt(CtxBox.Text, out var contextSize)
            || !TryParseNonNegativeInt(GpuBox.Text, out var gpuLayers)
            || !TryParsePositiveInt(ThreadsBox.Text, out var threads))
        {
            ProfileStatusLabel.Text = "Context and threads must be positive; GPU layers cannot be negative.";
            return;
        }

        var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var profile = _serverProfiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            profile = new ServerProfile { Name = name };
            _serverProfiles.Add(profile);
        }

        profile.Name = name;
        profile.ModelPath = selectedModel;
        profile.ContextSize = contextSize;
        profile.GpuLayers = gpuLayers;
        profile.Threads = threads;
        profile.FlashAttention = FlashAttnCheck.IsChecked == true;
        profile.Mlock = MlockCheck.IsChecked == true;

        RefreshProfileCombo(profile.Name);
        ProfileStatusLabel.Text = $"Saved profile '{profile.Name}'.";
        SaveSettings();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ServerProfile profile)
        {
            ProfileStatusLabel.Text = "Select a saved profile to delete.";
            return;
        }

        _serverProfiles.Remove(profile);
        RefreshProfileCombo();
        ProfileStatusLabel.Text = $"Deleted profile '{profile.Name}'.";
        SaveSettings();
    }

    private void RefreshProfileCombo(string? selectedName = null)
    {
        _updatingProfiles = true;
        try
        {
            ProfileCombo.ItemsSource = null;
            ProfileCombo.ItemsSource = _serverProfiles;
            if (!string.IsNullOrEmpty(selectedName))
            {
                ProfileCombo.SelectedItem = _serverProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                ProfileCombo.SelectedIndex = -1;
            }
        }
        finally
        {
            _updatingProfiles = false;
        }
    }

    private void ApplyServerProfile(ServerProfile profile)
    {
        ProfileNameBox.Text = profile.Name;
        CtxBox.Text = profile.ContextSize.ToString(CultureInfo.InvariantCulture);
        GpuBox.Text = profile.GpuLayers.ToString(CultureInfo.InvariantCulture);
        ThreadsBox.Text = profile.Threads.ToString(CultureInfo.InvariantCulture);
        FlashAttnCheck.IsChecked = profile.FlashAttention;
        MlockCheck.IsChecked = profile.Mlock;

        if (!string.IsNullOrWhiteSpace(profile.ModelPath))
        {
            var folder = Path.GetDirectoryName(profile.ModelPath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                ModelFolderBox.Text = folder;
                var modelItem = ModelCombo.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(
                        item.Tag?.ToString(), profile.ModelPath, StringComparison.OrdinalIgnoreCase));
                if (modelItem is not null)
                    ModelCombo.SelectedItem = modelItem;
            }
        }

        var serverRunning = _serverManaged && _serverProcess is { HasExited: false };
        ProfileStatusLabel.Text = serverRunning
            ? $"Applied '{profile.Name}'. Stop and start the server to use it."
            : $"Applied '{profile.Name}'.";
        StatusLabel.Text = serverRunning
            ? "Profile applied; restart the server to use the new settings"
            : $"Profile applied: {profile.Name}";
    }

    private static bool TryParsePositiveInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            && value > 0;
    }

    private static bool TryParseNonNegativeInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            && value >= 0;
    }

    private void RecommendQuant_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            QuantRecommendationLabel.Text = "Select a local GGUF model first.";
            return;
        }

        if (!TryParseCapacity(VramBox.Text, out var vramGiB) || vramGiB < 0
            || !TryParseCapacity(RamBox.Text, out var ramGiB) || ramGiB < 0)
        {
            QuantRecommendationLabel.Text = "Enter non-negative VRAM and RAM values in GB.";
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(selectedPath);
            if (string.IsNullOrEmpty(directory))
            {
                QuantRecommendationLabel.Text = "The selected model folder could not be read.";
                return;
            }

            var family = QuantRecommender.GetModelFamilyKey(Path.GetFileName(selectedPath));
            var variants = Directory.EnumerateFiles(directory, "*.gguf", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    Path = path,
                    FileName = Path.GetFileName(path),
                    Family = QuantRecommender.GetModelFamilyKey(Path.GetFileName(path))
                })
                .Where(file => string.Equals(file.Family, family, StringComparison.OrdinalIgnoreCase))
                .Select(file =>
                {
                    var info = new FileInfo(file.Path);
                    return new QuantModelFile(
                        file.FileName,
                        info.Length,
                        QuantRecommender.ParseQuant(file.FileName),
                        file.Path);
                });

            var result = QuantRecommender.Recommend(variants, vramGiB, ramGiB);
            if (result.HasRecommendation)
            {
                var file = result.SelectedFile!;
                QuantRecommendationLabel.Text =
                    $"Recommended: {file.FileName} ({file.Quant}, {file.SizeGiB:F2} GiB)\n" +
                    $"Estimated runtime memory: {result.EstimatedMemoryGiB!.Value:F2} GiB of {result.AvailableMemoryGiB:F2} GiB available.";
            }
            else
            {
                QuantRecommendationLabel.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            QuantRecommendationLabel.Text = $"Could not inspect model variants: {ex.Message}";
        }
    }

    private static bool TryParseCapacity(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ── Server management ────────────────────────────────────────────────
    private const string LatestLlamaReleaseUrl =
        "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";

    private async void CheckServerUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckServerUpdateBtn.IsEnabled = false;
        CheckServerUpdateBtn.Content = "Checking...";
        DownloadServerUpdateBtn.IsEnabled = false;
        ServerUpdateStatusLabel.Text = "Checking GitHub for the latest compatible llama.cpp build...";
        _serverUpdateCts?.Cancel();
        _serverUpdateCts = new CancellationTokenSource();
        CancelServerUpdateBtn.Visibility = Visibility.Visible;

        try
        {
            var localVersion = await DetectLocalServerVersionAsync(_serverUpdateCts.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestLlamaReleaseUrl);
            request.Headers.UserAgent.ParseAdd("LlamaLink/0.4");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, _serverUpdateCts.Token);
            response.EnsureSuccessStatusCode();
            var release = LlamaServerUpdater.ParseRelease(await response.Content.ReadAsStringAsync(_serverUpdateCts.Token));
            _latestServerRelease = release;

            var assets = release.Assets
                .OrderByDescending(asset => asset.Backend)
                .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ServerUpdateAssetCombo.ItemsSource = assets;

            var best = LlamaServerUpdater.SelectBestAsset(assets, LlamaHardwareCapabilities.Detect());
            ServerUpdateAssetCombo.SelectedItem = best;
            DownloadServerUpdateBtn.IsEnabled = best is not null;
            ServerVersionLabel.Text = localVersion is null
                ? "Local version: not found"
                : $"Local version: {localVersion}";

            ServerUpdateStatusLabel.Text = best is null
                ? $"Latest {release.TagName}; no compatible Windows x64 asset was found."
                : $"Latest {release.TagName}; selected {best.BackendLabel} for this PC.";
        }
        catch (OperationCanceledException)
        {
            ServerUpdateStatusLabel.Text = "Update check cancelled.";
        }
        catch (Exception ex)
        {
            ServerUpdateStatusLabel.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            CheckServerUpdateBtn.IsEnabled = true;
            CheckServerUpdateBtn.Content = "Check";
            CancelServerUpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<string?> DetectLocalServerVersionAsync(CancellationToken cancellationToken)
    {
        var exe = ExePathBox.Text.Trim();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };

        var started = false;
        try
        {
            process.Start();
            started = true;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            var output = await Task.WhenAll(stdoutTask, stderrTask);
            return LlamaServerUpdater.ExtractVersion(string.Join(Environment.NewLine, output));
        }
        catch (OperationCanceledException)
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
            throw;
        }
        catch
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
            return null;
        }
    }

    private async void DownloadServerUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_latestServerRelease is null || ServerUpdateAssetCombo.SelectedItem is not LlamaServerAsset asset)
        {
            ServerUpdateStatusLabel.Text = "Check for updates and select a compatible asset first.";
            return;
        }

        var safeTag = Regex.Replace(_latestServerRelease.TagName, @"[^A-Za-z0-9._-]", "_");
        var destinationFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "LlamaLink", "llama-server", safeTag);
        var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(asset.Name));
        var partialPath = destinationPath + ".part";

        try
        {
            Directory.CreateDirectory(destinationFolder);
            if (File.Exists(destinationPath)
                && (asset.SizeBytes <= 0 || new FileInfo(destinationPath).Length == asset.SizeBytes))
            {
                ServerUpdateStatusLabel.Text = $"Already downloaded: {destinationPath}";
                return;
            }

            _serverUpdateCts?.Cancel();
            _serverUpdateCts = new CancellationTokenSource();
            var token = _serverUpdateCts.Token;
            CheckServerUpdateBtn.IsEnabled = false;
            DownloadServerUpdateBtn.IsEnabled = false;
            CancelServerUpdateBtn.Visibility = Visibility.Visible;
            ServerUpdateProgress.Visibility = Visibility.Visible;
            ServerUpdateProgress.Value = 0;
            ServerUpdateStatusLabel.Text = $"Downloading {asset.Name}...";

            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("LlamaLink/0.4");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? asset.SizeBytes;
            var downloaded = 0L;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            await using var file = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 1024];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                downloaded += bytesRead;
                if (total > 0)
                    ServerUpdateProgress.Value = Math.Min(100, downloaded * 100.0 / total);
            }

            file.Close();
            File.Move(partialPath, destinationPath, true);
            ServerUpdateStatusLabel.Text = $"Downloaded {asset.Name} to {destinationFolder}";
        }
        catch (OperationCanceledException)
        {
            ServerUpdateStatusLabel.Text = "Download cancelled; partial file kept for retry.";
        }
        catch (Exception ex)
        {
            ServerUpdateStatusLabel.Text = $"Download failed: {ex.Message}";
        }
        finally
        {
            CheckServerUpdateBtn.IsEnabled = true;
            DownloadServerUpdateBtn.IsEnabled = ServerUpdateAssetCombo.SelectedItem is LlamaServerAsset;
            CancelServerUpdateBtn.Visibility = Visibility.Collapsed;
            ServerUpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelServerUpdate_Click(object sender, RoutedEventArgs e)
    {
        _serverUpdateCts?.Cancel();
    }

    private void ServerUpdateAsset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CheckServerUpdateBtn.IsEnabled) return;
        DownloadServerUpdateBtn.IsEnabled = ServerUpdateAssetCombo.SelectedItem is LlamaServerAsset;
    }

    private string GetServerUrl()
    {
        if (_serverManaged)
            return $"http://127.0.0.1:{PortBox.Text.Trim()}";
        return ExtUrlBox.Text.Trim().TrimEnd('/');
    }

    private LlamaBackendKind GetSelectedBackend()
    {
        if (_serverManaged) return LlamaBackendKind.LlamaCpp;
        return BackendAdapter.Parse((BackendCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "openai");
    }

    private static string FindLlamaServer()
    {
        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "llama-server.exe");
            if (File.Exists(candidate)) return candidate;
        }
        // Common locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = {
            Path.Combine(home, "llama.cpp", "build", "bin", "Release", "llama-server.exe"),
            Path.Combine(home, "llama.cpp", "build", "bin", "llama-server.exe"),
            Path.Combine(home, "llama.cpp", "llama-server.exe"),
            @"C:\llama.cpp\llama-server.exe",
            Path.Combine(home, "Desktop", "llama-server.exe"),
            Path.Combine(home, "Downloads", "llama-server.exe"),
        };
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            candidates = candidates.Append(Path.Combine(pf, "llama.cpp", "llama-server.exe")).ToArray();

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private void StartServer_Click(object sender, RoutedEventArgs e)
    {
        var exe = ExePathBox.Text.Trim();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            StatusLabel.Text = "ERROR: Invalid llama-server path";
            return;
        }

        var modelPath = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(modelPath))
        {
            StatusLabel.Text = "ERROR: No model selected";
            return;
        }

        var port = PortBox.Text.Trim();
        var args = new List<string>
        {
            "-m", modelPath,
            "--port", port,
            "-c", CtxBox.Text.Trim(),
            "-ngl", GpuBox.Text.Trim(),
            "-t", ThreadsBox.Text.Trim()
        };
        if (FlashAttnCheck.IsChecked == true) args.Add("-fa");
        if (MlockCheck.IsChecked == true) args.Add("--mlock");

        ServerLogBox.Clear();
        _serverManaged = true;
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;
        ServerStatusLabel.Text = "Starting...";
        ServerDot.Fill = FindResource("YellowBrush") as SolidColorBrush;
        StatusLabel.Text = "Starting server...";

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            _serverProcess = new Process { StartInfo = startInfo };
            _serverProcess.Start();

            var cmdLine = $"$ {exe} {startInfo.Arguments}\n";
            Dispatcher.Invoke(() => AppendServerLog(cmdLine));

            _serverThread = new Thread(() => ReadServerOutput(_serverProcess))
            { IsBackground = true };
            _serverThread.Start();
        }
        catch (Exception ex)
        {
            OnServerError(ex.Message);
        }
    }

    private void ReadServerOutput(Process proc)
    {
        bool readyEmitted = false;
        try
        {
            // Read both stdout and stderr
            var stdoutTask = Task.Run(() =>
            {
                while (!proc.StandardOutput.EndOfStream)
                {
                    var line = proc.StandardOutput.ReadLine();
                    if (line == null) break;
                    Dispatcher.Invoke(() => AppendServerLog(line));
                    if (!readyEmitted && (line.Contains("listening", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("server is listening", StringComparison.OrdinalIgnoreCase)))
                    {
                        readyEmitted = true;
                        Dispatcher.Invoke(OnServerReady);
                    }
                }
            });

            var stderrTask = Task.Run(() =>
            {
                while (!proc.StandardError.EndOfStream)
                {
                    var line = proc.StandardError.ReadLine();
                    if (line == null) break;
                    Dispatcher.Invoke(() => AppendServerLog(line));
                    if (!readyEmitted && (line.Contains("listening", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("server is listening", StringComparison.OrdinalIgnoreCase)))
                    {
                        readyEmitted = true;
                        Dispatcher.Invoke(OnServerReady);
                    }
                }
            });

            Task.WaitAll(stdoutTask, stderrTask);
            proc.WaitForExit();
        }
        catch { }

        Dispatcher.Invoke(OnServerStopped);
    }

    private void AppendServerLog(string text)
    {
        ServerLogBox.AppendText(text + "\n");
        ServerLogBox.ScrollToEnd();
    }

    private async void ConnectExternal_Click(object sender, RoutedEventArgs e)
    {
        var url = ExtUrlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url))
        {
            StatusLabel.Text = "ERROR: Enter a server URL";
            return;
        }

        var backend = GetSelectedBackend();

        ServerStatusLabel.Text = "Connecting...";
        ServerDot.Fill = FindResource("YellowBrush") as SolidColorBrush;
        StatusLabel.Text = $"Connecting to {url}...";

        try
        {
            var healthUrl = BackendAdapter.BuildEndpoint(url, BackendAdapter.GetHealthPath(backend));
            var resp = await _http.GetAsync(healthUrl, new CancellationTokenSource(5000).Token);
            if (resp.IsSuccessStatusCode)
            {
                _serverManaged = false;
                ConnectBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;
                OnServerReady();
                _healthTimer.Start();
                AppendServerLog($"Connected to {backend}: {url}");
            }
            else
            {
                ServerStatusLabel.Text = "Error";
                ServerDot.Fill = FindResource("RedBrush") as SolidColorBrush;
                StatusLabel.Text = $"Server returned HTTP {(int)resp.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            ServerStatusLabel.Text = "Error";
            ServerDot.Fill = FindResource("RedBrush") as SolidColorBrush;
            StatusLabel.Text = ex is HttpRequestException ? $"Cannot connect to {url}" : ex.Message;
        }
    }

    private void StopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_serverManaged && _serverProcess != null && !_serverProcess.HasExited)
        {
            try { _serverProcess.Kill(true); } catch { }
            StatusLabel.Text = "Stopping server...";
        }
        else
        {
            _healthTimer.Stop();
            OnServerStopped();
            StatusLabel.Text = "Disconnected from external server";
        }
    }

    private void OnServerReady()
    {
        ServerStatusLabel.Text = "Running";
        ServerDot.Fill = FindResource("GreenBrush") as SolidColorBrush;
        StatusLabel.Text = "Server is ready";

        if (_messages.Count > 0
            && !string.Equals(_chatAttachedContext, GetCurrentChatContext(), StringComparison.Ordinal))
        {
            AttachChatToCurrentServer();
        }
        else
        {
            UpdateChatContextLabel();
        }
    }

    private void OnServerError(string msg)
    {
        ServerStatusLabel.Text = "Error";
        ServerDot.Fill = FindResource("RedBrush") as SolidColorBrush;
        StatusLabel.Text = $"Server error: {msg}";
        StartBtn.IsEnabled = true;
        ConnectBtn.IsEnabled = true;
        StopBtn.IsEnabled = false;
    }

    private void OnServerStopped()
    {
        ServerStatusLabel.Text = "Stopped";
        ServerDot.Fill = FindResource("RedBrush") as SolidColorBrush;
        StartBtn.IsEnabled = true;
        ConnectBtn.IsEnabled = true;
        StopBtn.IsEnabled = false;
        StatusLabel.Text = "Server stopped";
        UpdateChatContextLabel();
    }

    private async Task CheckServerHealth()
    {
        if (_serverManaged) return;
        try
        {
            var healthUrl = BackendAdapter.BuildEndpoint(GetServerUrl(), BackendAdapter.GetHealthPath(GetSelectedBackend()));
            var resp = await _http.GetAsync(healthUrl,
                new CancellationTokenSource(3000).Token);
            if (!resp.IsSuccessStatusCode)
            {
                _healthTimer.Stop();
                OnServerStopped();
            }
        }
        catch
        {
            _healthTimer.Stop();
            OnServerStopped();
        }
    }

    // ── Chat ─────────────────────────────────────────────────────────────
    private string GetCurrentChatContext()
    {
        if (!_serverManaged)
            return ChatServerContext.ForExternal(GetServerUrl());

        if (ProfileCombo.SelectedItem is ServerProfile profile && !string.IsNullOrWhiteSpace(profile.Name))
            return ChatServerContext.ForProfile(profile.Name);

        return ChatServerContext.ForLocal((ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");
    }

    private bool IsServerReady => string.Equals(
        ServerStatusLabel.Text, "Running", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<SafeToolDefinition> GetEnabledToolDefinitions()
    {
        if (ToolsEnabledCheck.IsChecked != true)
        {
            ToolStatusLabel.Text = "Tools are disabled for this chat.";
            return Array.Empty<SafeToolDefinition>();
        }

        var definitions = SafeToolRegistry.GetDefinitions(
            FileReadToolCheck.IsChecked == true,
            CalculatorToolCheck.IsChecked == true,
            PythonToolCheck.IsChecked == true,
            WebSearchToolCheck.IsChecked == true);
        ToolStatusLabel.Text = definitions.Count == 0
            ? "Enable at least one tool before sending a tool-enabled request."
            : $"Enabled: {string.Join(", ", definitions.Select(definition => definition.Name))}. Confirmation is required.";
        return definitions;
    }

    private WebSearchOptions GetWebSearchOptions()
    {
        var provider = (WebSearchProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "duckduckgo";
        return new WebSearchOptions(
            ToolsEnabledCheck.IsChecked == true && WebSearchToolCheck.IsChecked == true,
            provider,
            WebSearchEndpointBox.Text.Trim(),
            5);
    }

    private bool AttachChatToCurrentServer()
    {
        if (!IsServerReady)
        {
            UpdateChatContextLabel();
            StatusLabel.Text = "Start or connect to a server before continuing this chat";
            return false;
        }

        var previousContext = _chatAttachedContext;
        _chatAttachedContext = GetCurrentChatContext();
        UpdateChatContextLabel();

        if (_messages.Count > 0)
        {
            SaveCurrentChat();
            StatusLabel.Text = previousContext is not null
                && !string.Equals(previousContext, _chatAttachedContext, StringComparison.Ordinal)
                ? $"Continued on {_chatAttachedContext}; {_messages.Count(m => m["role"] != "system")} messages preserved"
                : $"Attached to {_chatAttachedContext}";
        }

        return true;
    }

    private void UpdateChatContextLabel()
    {
        if (_messages.Count == 0)
        {
            ChatContextLabel.Text = "New conversation";
            ChatServerActionBtn.Content = "Continue here";
            ChatServerActionBtn.IsEnabled = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(_chatAttachedContext))
        {
            ChatContextLabel.Text = "Detached — conversation stays loaded";
            ChatServerActionBtn.Content = "Continue here";
            ChatServerActionBtn.IsEnabled = IsServerReady;
        }
        else
        {
            var count = _messages.Count(message => message["role"] != "system");
            ChatContextLabel.Text = $"{_chatAttachedContext} · {count} msgs";
            ChatServerActionBtn.Content = "Detach";
            ChatServerActionBtn.IsEnabled = true;
        }
    }

    private void ChatServerAction_Click(object sender, RoutedEventArgs e)
    {
        if (_messages.Count == 0) return;

        if (string.IsNullOrWhiteSpace(_chatAttachedContext))
        {
            AttachChatToCurrentServer();
            return;
        }

        _chatAttachedContext = null;
        UpdateChatContextLabel();
        SaveCurrentChat();
        StatusLabel.Text = "Chat detached; its messages remain available for another server";
    }

    private SpeechToolPaths GetSpeechToolPaths()
        => new(
            SpeechFfmpegBox.Text.Trim(),
            SpeechWhisperBox.Text.Trim(),
            SpeechWhisperModelBox.Text.Trim(),
            SpeechPiperBox.Text.Trim(),
            SpeechPiperVoiceBox.Text.Trim(),
            SpeechMicBox.Text.Trim());

    private string GetSpeechOutputDirectory()
    {
        var directory = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "speech");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void SpeechRecord_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_speechRecorder is not null)
            return;

        try
        {
            SaveSettings();
            _speechAudioPath = Path.Combine(
                GetSpeechOutputDirectory(),
                $"recording_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.wav");
            _speechRecorder = SpeechToolRunner.StartRecording(GetSpeechToolPaths(), _speechAudioPath);
            SpeechRecordBtn.IsEnabled = false;
            SpeechStopBtn.IsEnabled = true;
            SpeechStatusLabel.Text = "Recording... release the button or press Stop.";
        }
        catch (Exception ex)
        {
            _speechRecorder = null;
            SpeechStatusLabel.Text = $"Unable to record: {ex.Message}";
        }
        e.Handled = true;
    }

    private async void SpeechRecord_MouseUp(object sender, MouseButtonEventArgs e)
    {
        await StopAndTranscribeSpeechAsync();
        e.Handled = true;
    }

    private async void SpeechStop_Click(object sender, RoutedEventArgs e)
        => await StopAndTranscribeSpeechAsync();

    private async Task StopAndTranscribeSpeechAsync()
    {
        var recorder = _speechRecorder;
        _speechRecorder = null;
        SpeechRecordBtn.IsEnabled = true;
        SpeechStopBtn.IsEnabled = false;
        if (recorder is null)
            return;

        try
        {
            if (!recorder.HasExited)
                recorder.Kill(true);
            await recorder.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            SpeechStatusLabel.Text = $"Recording stopped with an error: {ex.Message}";
            return;
        }
        finally
        {
            recorder.Dispose();
        }

        if (string.IsNullOrWhiteSpace(_speechAudioPath) || !File.Exists(_speechAudioPath))
        {
            SpeechStatusLabel.Text = "No WAV was produced; check ffmpeg and the microphone name.";
            return;
        }

        _speechCts?.Cancel();
        _speechCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SpeechStatusLabel.Text = "Transcribing with whisper.cpp...";
        try
        {
            var result = await SpeechToolRunner.TranscribeAsync(
                GetSpeechToolPaths(),
                _speechAudioPath,
                _speechCts.Token);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
            {
                InputBox.Text = string.IsNullOrWhiteSpace(InputBox.Text)
                    ? result.Content
                    : $"{InputBox.Text.Trim()}\n{result.Content}";
                SpeechStatusLabel.Text = "Transcription inserted into the composer.";
            }
            else
            {
                SpeechStatusLabel.Text = result.Success ? "Whisper returned no text." : result.Content;
            }
        }
        catch (OperationCanceledException)
        {
            SpeechStatusLabel.Text = "Speech transcription cancelled.";
        }
    }

    private async void SpeechSynthesize_Click(object sender, RoutedEventArgs e)
    {
        var text = _messages.LastOrDefault(message => message["role"] == "assistant")?["content"];
        if (string.IsNullOrWhiteSpace(text))
        {
            SpeechStatusLabel.Text = "Generate an assistant response before making a WAV.";
            return;
        }

        var outputPath = Path.Combine(
            GetSpeechOutputDirectory(),
            $"reply_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.wav");
        _speechCts?.Cancel();
        _speechCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SpeechStatusLabel.Text = "Synthesizing with Piper...";
        try
        {
            var result = await SpeechToolRunner.SynthesizeAsync(
                GetSpeechToolPaths(),
                text,
                outputPath,
                _speechCts.Token);
            SpeechStatusLabel.Text = result.Success
                ? $"WAV created: {result.OutputPath}"
                : result.Content;
        }
        catch (OperationCanceledException)
        {
            SpeechStatusLabel.Text = "Speech synthesis cancelled.";
        }
    }

    private static string[] SupportedRagPaths(IEnumerable<string> paths)
        => paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(RagTextExtractor.IsSupported)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void RefreshRagSources()
    {
        if (RagSourceList is null)
            return;

        var sources = _ragIndex.SourcePaths.ToList();
        RagSourceList.ItemsSource = sources
            .Select(path => $"{Path.GetFileName(path)}\n{path}")
            .ToList();
        RagStatusLabel.Text = sources.Count == 0
            ? "Drop .pdf, .md, or .txt files here to build a local index."
            : $"{sources.Count} document{(sources.Count == 1 ? "" : "s")} · {_ragIndex.ChunkCount} chunks · local embeddings";
    }

    private void ClearRagRetrieval()
    {
        _lastRagMatches.Clear();
        RagRetrievedList.ItemsSource = null;
        RagSelectedSourceLabel.Text = "No retrieval selected.";
        RagExcerptViewer.Clear();
    }

    private void RefreshRagViewer()
    {
        RagRetrievedList.ItemsSource = _lastRagMatches.ToList();
        if (_lastRagMatches.Count > 0)
            RagRetrievedList.SelectedIndex = 0;
        else
            ClearRagRetrieval();
    }

    private void RagRetrieved_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (RagRetrievedList.SelectedItem is not RagSearchResult result)
        {
            RagSelectedSourceLabel.Text = "No retrieval selected.";
            RagExcerptViewer.Clear();
            return;
        }

        RagSelectedSourceLabel.Text = $"{result.SourceName} · chunk {result.ChunkIndex + 1} · relevance {result.Score:F2}";
        RagExcerptViewer.Text = result.Text;
    }

    private void RagAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Documents (*.pdf;*.md;*.txt)|*.pdf;*.md;*.txt|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            IndexRagFilesAsync(dialog.FileNames);
    }

    private void RagFiles_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void RagFiles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            IndexRagFilesAsync(paths);
    }

    private async void IndexRagFilesAsync(IEnumerable<string> paths)
        => await IndexRagFilesCoreAsync(SupportedRagPaths(paths), fromWatcher: false);

    private async Task IndexRagFilesCoreAsync(string[] candidates, bool fromWatcher)
    {
        if (_ragIndexing)
            return;

        if (candidates.Length == 0)
        {
            if (!fromWatcher)
                RagStatusLabel.Text = "Choose or drop at least one .pdf, .md, or .txt file.";
            return;
        }

        ClearRagRetrieval();
        _ragIndexing = true;
        RagAddBtn.IsEnabled = false;
        RagClearBtn.IsEnabled = false;
        RagStatusLabel.Text = fromWatcher
            ? $"Syncing {candidates.Length} changed document{(candidates.Length == 1 ? "" : "s")}..."
            : $"Indexing {candidates.Length} document{(candidates.Length == 1 ? "" : "s")}...";
        try
        {
            var result = await Task.Run(() => _ragIndex.IndexFiles(candidates));
            RagIndexStore.Save(_ragIndexPath, _ragIndex);
            RefreshRagSources();
            var errorText = result.Errors.Count == 0
                ? ""
                : $" Errors: {string.Join(" | ", result.Errors)}";
            var watchText = _ragWatcher is null ? "" : $" Watching {Path.GetFileName(_ragWatchedFolder)}.";
            RagStatusLabel.Text = $"Indexed {result.FilesIndexed} file{(result.FilesIndexed == 1 ? "" : "s")} " +
                $"into {result.ChunksIndexed} chunks.{watchText}{errorText}";
        }
        catch (Exception ex)
        {
            RagStatusLabel.Text = $"RAG indexing failed: {ex.Message}";
        }
        finally
        {
            _ragIndexing = false;
            RagAddBtn.IsEnabled = true;
            RagClearBtn.IsEnabled = true;
        }
    }

    private void RagBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to keep indexed" };
        if (dialog.ShowDialog() == true)
            RagFolderBox.Text = dialog.FolderName;
    }

    private void RagWatch_Click(object sender, RoutedEventArgs e)
        => StartRagFolderWatch(RagFolderBox.Text.Trim());

    private void RagStopWatch_Click(object sender, RoutedEventArgs e)
        => StopRagFolderWatch(report: true);

    private void StartRagFolderWatch(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            RagStatusLabel.Text = "Choose a folder before starting the watcher.";
            return;
        }

        try
        {
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder))
            {
                RagStatusLabel.Text = "The selected RAG folder does not exist.";
                return;
            }

            StopRagFolderWatch(report: false);
            _ragWatchedFolder = folder;
            RagFolderBox.Text = folder;
            _ragWatcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*",
                EnableRaisingEvents = true,
            };
            _ragWatcher.Created += RagFolder_Changed;
            _ragWatcher.Changed += RagFolder_Changed;
            _ragWatcher.Deleted += RagFolder_Deleted;
            _ragWatcher.Renamed += RagFolder_Renamed;
            RagWatchBtn.IsEnabled = false;
            RagStopWatchBtn.IsEnabled = true;

            foreach (var stalePath in _ragIndex.SourcePaths
                .Where(path => IsPathWithin(path, folder) && !File.Exists(path))
                .ToList())
                _ragIndex.RemoveSource(stalePath);
            RagIndexStore.Save(_ragIndexPath, _ragIndex);

            var initialFiles = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(RagTextExtractor.IsSupported)
                .ToArray();
            IndexRagFilesAsync(initialFiles);
        }
        catch (Exception ex)
        {
            StopRagFolderWatch(report: false);
            RagStatusLabel.Text = $"Unable to watch folder: {ex.Message}";
        }
    }

    private void StopRagFolderWatch(bool report)
    {
        _ragSyncTimer.Stop();
        _ragPendingPaths.Clear();
        if (_ragWatcher is not null)
        {
            _ragWatcher.EnableRaisingEvents = false;
            _ragWatcher.Dispose();
            _ragWatcher = null;
        }
        _ragWatchedFolder = null;
        if (RagWatchBtn is not null)
            RagWatchBtn.IsEnabled = true;
        if (RagStopWatchBtn is not null)
            RagStopWatchBtn.IsEnabled = false;
        if (report)
            RagStatusLabel.Text = "Folder watching stopped; the current index is retained.";
    }

    private void RagFolder_Changed(object sender, FileSystemEventArgs e)
    {
        if (!RagTextExtractor.IsSupported(e.FullPath))
            return;

        try
        {
            Dispatcher.InvokeAsync(() =>
            {
                _ragPendingPaths.Add(Path.GetFullPath(e.FullPath));
                _ragSyncTimer.Stop();
                _ragSyncTimer.Start();
                RagStatusLabel.Text = "Folder change detected; sync queued...";
            });
        }
        catch (InvalidOperationException) { }
    }

    private void RagFolder_Deleted(object sender, FileSystemEventArgs e)
    {
        if (!RagTextExtractor.IsSupported(e.FullPath))
            return;

        try
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_ragIndex.RemoveSource(e.FullPath) > 0)
                {
                    RagIndexStore.Save(_ragIndexPath, _ragIndex);
                    RefreshRagSources();
                    RagStatusLabel.Text = $"Removed deleted source {Path.GetFileName(e.FullPath)}.";
                }
            });
        }
        catch (InvalidOperationException) { }
    }

    private void RagFolder_Renamed(object sender, RenamedEventArgs e)
    {
        RagFolder_Deleted(sender, e);
        RagFolder_Changed(sender, e);
    }

    private async Task SyncWatchedRagFolderAsync()
    {
        if (_ragWatcher is null || _ragPendingPaths.Count == 0)
            return;
        if (_ragIndexing)
        {
            _ragSyncTimer.Start();
            return;
        }

        var paths = _ragPendingPaths.ToArray();
        _ragPendingPaths.Clear();
        await IndexRagFilesCoreAsync(SupportedRagPaths(paths), fromWatcher: true);
    }

    private static bool IsPathWithin(string path, string folder)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
    }

    private void RagClear_Click(object sender, RoutedEventArgs e)
    {
        if (_ragIndexing)
            return;

        ClearRagRetrieval();
        _ragIndex.Clear();
        RagIndexStore.Save(_ragIndexPath, _ragIndex);
        RefreshRagSources();
        RagStatusLabel.Text = "RAG index cleared; source files were not deleted.";
    }

    private List<ChatHistoryMessage> BuildPayloadMessagesWithRag(List<ChatHistoryMessage>? seedMessages = null)
    {
        var messages = seedMessages ?? BuildChatHistoryMessages();

        if (RagEnabledCheck.IsChecked != true || _ragIndex.ChunkCount == 0)
        {
            ClearRagRetrieval();
            return messages;
        }

        var query = messages.LastOrDefault(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;
        var topK = int.TryParse(RagTopKBox.Text, out var parsedTopK) ? Math.Clamp(parsedTopK, 1, 12) : 4;
        var matches = _ragIndex.Search(query ?? "", topK);
        if (matches.Count == 0)
        {
            ClearRagRetrieval();
            RagStatusLabel.Text = "RAG enabled, but no indexed excerpt matched this question.";
            return messages;
        }

        _lastRagMatches = matches;
        RefreshRagViewer();
        var context = RagIndex.FormatContext(matches);
        var system = messages.FirstOrDefault(message =>
            string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase));
        if (system is null)
        {
            messages.Insert(0, new ChatHistoryMessage
            {
                Role = "system",
                Content = $"Local RAG context:\n{context}",
            });
        }
        else
        {
            system.Content = $"{system.Content.Trim()}\n\nLocal RAG context:\n{context}".Trim();
        }

        RagStatusLabel.Text = $"Retrieved {matches.Count} local excerpt{(matches.Count == 1 ? "" : "s")} for this prompt.";
        return messages;
    }

    private List<ChatHistoryMessage> BuildChatHistoryMessages()
    {
        return _messages.Select((message, index) =>
        {
            var historyMessage = new ChatHistoryMessage
            {
                Role = message["role"],
                Content = message["content"],
            };
            if (_messageImages.TryGetValue(index, out var images))
                historyMessage.Images = VisionImageStore.CloneAll(images).ToList();
            return historyMessage;
        }).ToList();
    }

    private void RefreshImageAttachments()
    {
        if (ImageAttachmentPanel is null)
            return;

        ImageAttachmentPanel.Children.Clear();
        ImageAttachmentPanel.Visibility = _pendingImages.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        foreach (var attachment in _pendingImages)
        {
            var removeButton = new Button
            {
                Content = $"× {attachment.DisplayName}",
                Tag = attachment,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 5, 4),
                ToolTip = "Remove image attachment",
                Style = (Style)FindResource("GhostButton"),
            };
            removeButton.Click += RemovePendingImage_Click;
            ImageAttachmentPanel.Children.Add(removeButton);
        }
    }

    private void ChatImage_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void ChatImage_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            AddPendingImages(paths);
    }

    private void AddPendingImages(IEnumerable<string> paths)
    {
        if (_streaming)
        {
            StatusLabel.Text = "Wait for the current response before attaching images";
            return;
        }

        var added = 0;
        var rejected = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var attachment = VisionImageStore.Create(path);
                if (_pendingImages.Any(existing => string.Equals(existing.Path, attachment.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _pendingImages.Add(attachment);
                added++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                rejected.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        RefreshImageAttachments();
        if (added > 0)
            StatusLabel.Text = $"Attached {added} image{(added == 1 ? "" : "s")} to the next message";
        if (rejected.Count > 0)
            StatusLabel.Text += $". Rejected: {string.Join(" | ", rejected)}";
    }

    private void RemovePendingImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatImageAttachment attachment })
        {
            _pendingImages.Remove(attachment);
            RefreshImageAttachments();
            StatusLabel.Text = "Image attachment removed";
        }
    }

    private ImageGenerationSettings GetImageGenerationSettings()
    {
        var outputDirectory = ImageGenOutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "generated");
        return new ImageGenerationSettings(
            ImageGenExeBox.Text.Trim(),
            ImageGenModelBox.Text.Trim(),
            outputDirectory,
            int.TryParse(ImageGenStepsBox.Text, out var steps) ? steps : 20,
            int.TryParse(ImageGenWidthBox.Text, out var width) ? width : 512,
            int.TryParse(ImageGenHeightBox.Text, out var height) ? height : 512);
    }

    private async Task GenerateImageCommandAsync(string prompt)
    {
        if (ImageGenEnabledCheck.IsChecked != true)
        {
            ImageGenStatusLabel.Text = "Enable image generation before using /image.";
            return;
        }
        if (_pendingImages.Count > 0)
        {
            ImageGenStatusLabel.Text = "Send or remove pending image attachments before using /image.";
            return;
        }

        prompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ImageGenStatusLabel.Text = "Use /image followed by a prompt.";
            return;
        }

        var command = $"/image {prompt}";
        InputBox.Clear();
        _messages.Add(new() { ["role"] = "user", ["content"] = command });
        _chatMessages.Add(MakeChatVM("user", command));
        EmptyState.Visibility = Visibility.Collapsed;
        ScrollChatToBottom();
        SaveCurrentChat();

        _imageGenCts?.Cancel();
        _imageGenCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        ImageGenStatusLabel.Text = "Generating image...";
        try
        {
            var result = await ImageGenerationService.GenerateAsync(
                GetImageGenerationSettings(),
                prompt,
                _imageGenCts.Token);
            if (result.Success && result.OutputPath is not null)
            {
                var content = $"Generated image:\n{result.OutputPath}";
                _messages.Add(new() { ["role"] = "assistant", ["content"] = content });
                _chatMessages.Add(MakeChatVM("assistant", content));
                ImageGenStatusLabel.Text = $"Image generated: {result.OutputPath}";
                SaveCurrentChat();
            }
            else
            {
                ImageGenStatusLabel.Text = result.Message;
                StatusLabel.Text = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            ImageGenStatusLabel.Text = "Image generation cancelled.";
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => SendMessage();
    private void Regenerate_Click(object sender, RoutedEventArgs e) => SendMessage(regenerate: true);
    private void StopGen_Click(object sender, RoutedEventArgs e) => _streamCts?.Cancel();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            SendMessage();
        }
    }

    private async void SendMessage(bool regenerate = false)
    {
        var text = InputBox.Text.Trim();
        var hasPendingImages = _pendingImages.Count > 0;
        if ((!regenerate && string.IsNullOrEmpty(text) && !hasPendingImages) || _streaming)
            return;

        if (!regenerate && (text.Equals("/image", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("/image ", StringComparison.OrdinalIgnoreCase)))
        {
            await GenerateImageCommandAsync(text.Length > 6 ? text[6..] : "");
            return;
        }

        if (regenerate && hasPendingImages)
        {
            StatusLabel.Text = "Send the attached image before regenerating the previous response";
            return;
        }

        if (regenerate)
        {
            var history = _messages.Select(message => new ChatHistoryMessage
            {
                Role = message.TryGetValue("role", out var role) ? role : "",
                Content = message.TryGetValue("content", out var content) ? content : "",
            }).ToList();
            if (!ChatRegenerator.CanRegenerate(history))
            {
                StatusLabel.Text = "A completed assistant response is required to regenerate";
                RefreshForkMessageOptions();
                return;
            }
        }

        if (!IsServerReady)
        {
            StatusLabel.Text = "Start or connect to a server before sending a message";
            return;
        }

        if (string.IsNullOrWhiteSpace(_chatAttachedContext) && !AttachChatToCurrentServer())
            return;

        var backend = GetSelectedBackend();
        var model = _serverManaged ? "" : ExtModelBox.Text.Trim();
        if (BackendAdapter.RequiresModel(backend) && string.IsNullOrWhiteSpace(model))
        {
            StatusLabel.Text = "Enter the Ollama model name before sending a message";
            return;
        }

        if (regenerate)
        {
            _regeneratingOriginal = new Dictionary<string, string>(_messages[^1]);
            _messages.RemoveAt(_messages.Count - 1);
            if (_chatMessages.Count > 0 && _chatMessages[^1].RoleLabel == "Assistant")
                _chatMessages.RemoveAt(_chatMessages.Count - 1);
            RefreshForkMessageOptions();
        }
        else
        {
            var messageImages = VisionImageStore.CloneAll(_pendingImages);
            if (string.IsNullOrWhiteSpace(text) && messageImages.Count > 0)
                text = "Describe the attached image.";
            InputBox.Clear();

            if (_messages.Count == 0)
            {
                var sysPrompt = SystemPromptBox.Text.Trim();
                if (!string.IsNullOrEmpty(sysPrompt))
                {
                    _messages.Add(new() { ["role"] = "system", ["content"] = sysPrompt });
                    _chatMessages.Add(MakeChatVM("system", sysPrompt));
                }
            }

            _messages.Add(new() { ["role"] = "user", ["content"] = text });
            if (messageImages.Count > 0)
                _messageImages[_messages.Count - 1] = messageImages;
            _chatMessages.Add(MakeChatVM("user", FormatChatContent(text, messageImages)));
            _pendingImages.Clear();
            RefreshImageAttachments();
        }
        EmptyState.Visibility = Visibility.Collapsed;
        ScrollChatToBottom();

        double temp = TempSlider.Value / 100.0;
        double topP = TopPSlider.Value / 100.0;
        double repPenalty = RepSlider.Value / 100.0;
        int.TryParse(TopKBox.Text, out int topK);
        int.TryParse(MaxTokensBox.Text, out int maxTokens);

        var payloadMessages = BuildPayloadMessagesWithRag();
        var toolDefinitions = GetEnabledToolDefinitions();
        var tokenProbabilityOptions = GetTokenProbabilityOptions();
        var payload = BackendAdapter.BuildPayload(
            backend, model, payloadMessages, temp, topP, topK, repPenalty, maxTokens,
            tools: toolDefinitions, grammar: GetGrammarConstraint(), tokenProbabilities: tokenProbabilityOptions);

        _streaming = true;
        RefreshForkMessageOptions();
        _streamBuffer = "";
        _streamDirty = false;
        _tokenCount = 0;
        _streamStartTime = Stopwatch.GetTimestamp();
        _activeTokenProbabilityOptions = tokenProbabilityOptions;
        _activeTokenProbabilityBackendSupports = backend != LlamaBackendKind.Ollama;
        _tokenProbabilityRows.Clear();
        TokenProbabilityStatusLabel.Text = !_activeTokenProbabilityOptions.Enabled
            ? "Token probabilities are disabled."
            : _activeTokenProbabilityBackendSupports
                ? $"Collecting top {_activeTokenProbabilityOptions.ClampedTopK} alternatives..."
                : "The Ollama API does not expose token probabilities.";
        SendBtn.Visibility = Visibility.Collapsed;
        StopGenBtn.Visibility = Visibility.Visible;
        SpeedLabel.Text = "";

        // Add placeholder assistant message
        var assistantVM = MakeChatVM("assistant", "Thinking...");
        _chatMessages.Add(assistantVM);
        ScrollChatToBottom();

        _streamCts = new CancellationTokenSource();
        _toolCallAccumulator = new ToolCallAccumulator();
        _streamTimer.Start();
        StatusLabel.Text = "Generating response...";

        try
        {
            var url = BackendAdapter.BuildEndpoint(GetServerUrl(), BackendAdapter.GetChatPath(backend));
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _streamCts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(_streamCts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                if (_streamCts.IsCancellationRequested) break;

                var line = await reader.ReadLineAsync(_streamCts.Token);
                if (line == null) break;
                var part = BackendAdapter.ParseStreamLine(backend, line);
                if (part is null) continue;
                if (part.ToolCalls is { Count: > 0 })
                    _toolCallAccumulator.Add(part.ToolCalls);
                if (part.TokenProbabilities is { Count: > 0 } probabilities)
                {
                    foreach (var probability in probabilities)
                    {
                        while (_tokenProbabilityRows.Count >= 512)
                            _tokenProbabilityRows.RemoveAt(0);
                        _tokenProbabilityRows.Add(TokenProbabilityFormatting.Format(probability));
                    }
                    _tokenCount += probabilities.Count;
                    TokenProbabilityStatusLabel.Text =
                        $"Showing {_tokenProbabilityRows.Count} token(s); top {_activeTokenProbabilityOptions.ClampedTopK} alternatives";
                    if (_tokenProbabilityRows.Count > 0)
                        TokenProbabilityList.ScrollIntoView(_tokenProbabilityRows[^1]);
                }
                else if (!string.IsNullOrEmpty(part.Content))
                {
                    _tokenCount++;
                }
                if (part.Done) break;

                if (!string.IsNullOrEmpty(part.Content))
                {
                    _streamBuffer += part.Content;
                    _streamDirty = true;
                }
            }

            OnResponseDone();
        }
        catch (OperationCanceledException)
        {
            OnResponseDone();
        }
        catch (Exception ex)
        {
            OnChatError(ex.Message);
        }
    }

    private void FlushStream()
    {
        if (!_streamDirty) return;
        _streamDirty = false;

        var elapsed = Stopwatch.GetElapsedTime(_streamStartTime).TotalSeconds;
        if (elapsed > 0.5)
        {
            var tps = _tokenCount / elapsed;
            SpeedLabel.Text = $"{tps:F1} tok/s";
        }

        // Update the last (assistant) message in the collection
        if (_chatMessages.Count > 0)
        {
            var last = _chatMessages[^1];
            last.Content = _streamBuffer;
            // Force UI refresh by replacing the item
            _chatMessages[^1] = new ChatMessageVM
            {
                RoleLabel = last.RoleLabel,
                Content = _streamBuffer,
                Accent = last.Accent,
                Background = last.Background
            };
        }
        ScrollChatToBottom();
    }

    private void OnResponseDone()
    {
        _streamTimer.Stop();
        _streaming = false;
        SendBtn.Visibility = Visibility.Visible;
        SendBtn.IsEnabled = true;
        StopGenBtn.Visibility = Visibility.Collapsed;

        var elapsed = Stopwatch.GetElapsedTime(_streamStartTime).TotalSeconds;
        if (elapsed > 0 && _tokenCount > 0)
        {
            var tps = _tokenCount / elapsed;
            SpeedLabel.Text = $"{tps:F1} tok/s ({_tokenCount} tokens in {elapsed:F1}s)";
        }

        if (!_activeTokenProbabilityOptions.Enabled)
            TokenProbabilityStatusLabel.Text = "Token probabilities are disabled.";
        else if (!_activeTokenProbabilityBackendSupports)
            TokenProbabilityStatusLabel.Text = "The Ollama API does not expose token probabilities.";
        else if (_tokenProbabilityRows.Count == 0)
            TokenProbabilityStatusLabel.Text = "The backend returned no token probabilities.";

        var toolCalls = _toolCallAccumulator?.Complete() ?? Array.Empty<ToolCallRequest>();
        _toolCallAccumulator = null;
        var restoringRegeneration = _regeneratingOriginal is not null;
        var assistantContent = _streamBuffer;
        if (toolCalls.Count > 0)
        {
            var summary = string.Join(
                "\n",
                toolCalls.Select(call => $"Tool request: {call.Name} {FormatToolArguments(call.ArgumentsJson)}"));
            assistantContent = string.IsNullOrWhiteSpace(assistantContent)
                ? summary
                : $"{assistantContent}\n\n{summary}";
        }

        if (!string.IsNullOrEmpty(assistantContent))
        {
            _messages.Add(new() { ["role"] = "assistant", ["content"] = assistantContent });
            _regeneratingOriginal = null;

            // Final update of assistant bubble
            if (_chatMessages.Count > 0)
            {
                _chatMessages[^1] = new ChatMessageVM
                {
                    RoleLabel = "Assistant",
                    Content = assistantContent,
                    Accent = AssistantAccent,
                    Background = AssistantBg
                };
            }
        }
        else
        {
            if (_chatMessages.Count > 0 && _chatMessages[^1].RoleLabel == "Assistant")
                _chatMessages.RemoveAt(_chatMessages.Count - 1);
            RestoreRegeneratedResponse();
        }

        var msgCount = _messages.Count(m => m["role"] != "system");
        TokenCountLabel.Text = $"{msgCount} messages";
        StatusLabel.Text = restoringRegeneration && string.IsNullOrEmpty(assistantContent)
            ? "No replacement response; previous response restored"
            : "Response complete";
        ScrollChatToBottom();

        foreach (var call in toolCalls)
            _pendingToolCalls.Enqueue(call);
        SaveCurrentChat();
        RefreshForkMessageOptions();
        ShowNextToolCall();
    }

    private static string FormatToolArguments(string argumentsJson)
    {
        var compact = argumentsJson.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length > 600 ? compact[..600] + "..." : compact;
    }

    private void ShowNextToolCall()
    {
        if (_pendingToolCalls.Count == 0)
        {
            ToolConfirmationPanel.Visibility = Visibility.Collapsed;
            SendBtn.IsEnabled = true;
            return;
        }

        var call = _pendingToolCalls.Peek();
        var scope = string.Equals(call.Name, "web_search", StringComparison.OrdinalIgnoreCase)
            ? $"Provider: {(WebSearchProviderCombo.SelectedItem as ComboBoxItem)?.Content}"
            : $"Safe root: {ToolRootBox.Text.Trim()}";
        ToolConfirmationLabel.Text =
            $"{call.Name}\nArguments: {FormatToolArguments(call.ArgumentsJson)}\n" +
            scope;
        ToolConfirmationPanel.Visibility = Visibility.Visible;
        SendBtn.IsEnabled = false;
        StatusLabel.Text = $"Confirm tool call: {call.Name}";
    }

    private async void ApproveTool_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingToolCalls.Count == 0) return;

        var call = _pendingToolCalls.Dequeue();
        ApproveToolBtn.IsEnabled = false;
        DenyToolBtn.IsEnabled = false;
        ToolConfirmationLabel.Text = $"Executing {call.Name}...";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await SafeToolExecutor.ExecuteAsync(
                call,
                ToolRootBox.Text.Trim(),
                timeout.Token,
                GetWebSearchOptions());
            AppendToolResult(call, result);
        }
        catch (OperationCanceledException)
        {
            AppendToolResult(call, ToolExecutionResult.Error("Tool execution timed out or was cancelled."));
        }
        finally
        {
            ApproveToolBtn.IsEnabled = true;
            DenyToolBtn.IsEnabled = true;
        }

        FinishOrContinueToolCalls();
    }

    private void DenyTool_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingToolCalls.Count == 0) return;
        var call = _pendingToolCalls.Dequeue();
        AppendToolResult(call, ToolExecutionResult.Error("The user denied this tool call."));
        FinishOrContinueToolCalls();
    }

    private void AppendToolResult(ToolCallRequest call, ToolExecutionResult result)
    {
        var prefix = result.Success ? "Tool result" : "Tool error";
        var content = $"{prefix} ({call.Name}):\n{result.Content}";
        _messages.Add(new() { ["role"] = "user", ["content"] = content });
        _chatMessages.Add(new ChatMessageVM
        {
            RoleLabel = prefix,
            Content = content,
            Accent = result.Success ? AssistantAccent : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)),
            Background = SystemBg,
        });
        TokenCountLabel.Text = $"{_messages.Count(message => message["role"] != "system")} messages";
        ScrollChatToBottom();
        SaveCurrentChat();
    }

    private void FinishOrContinueToolCalls()
    {
        if (_pendingToolCalls.Count > 0)
        {
            ShowNextToolCall();
            return;
        }

        ToolConfirmationPanel.Visibility = Visibility.Collapsed;
        SendBtn.IsEnabled = true;
        InputBox.Text = "Use the approved tool result to continue the task.";
        SendMessage();
    }

    private void RestoreRegeneratedResponse()
    {
        if (_regeneratingOriginal is null)
            return;

        var original = _regeneratingOriginal;
        _regeneratingOriginal = null;
        var role = original.TryGetValue("role", out var rawRole) ? rawRole : "assistant";
        var content = original.TryGetValue("content", out var rawContent) ? rawContent : "";
        _messages.Add(new Dictionary<string, string>
        {
            ["role"] = role,
            ["content"] = content,
        });
        _chatMessages.Add(MakeChatVM(role, content));
        TokenCountLabel.Text = $"{_messages.Count(message => message["role"] != "system")} messages";
        RefreshForkMessageOptions();
        ScrollChatToBottom();
    }

    private void OnChatError(string error)
    {
        _streamTimer.Stop();
        _streaming = false;
        SendBtn.Visibility = Visibility.Visible;
        SendBtn.IsEnabled = true;
        StopGenBtn.Visibility = Visibility.Collapsed;
        SpeedLabel.Text = "";
        _toolCallAccumulator = null;
        _pendingToolCalls.Clear();
        ToolConfirmationPanel.Visibility = Visibility.Collapsed;
        RefreshForkMessageOptions();

        // Remove the placeholder assistant message
        if (_chatMessages.Count > 0 && _chatMessages[^1].RoleLabel == "Assistant")
            _chatMessages.RemoveAt(_chatMessages.Count - 1);
        RestoreRegeneratedResponse();

        // Show error as a system message
        _chatMessages.Add(new ChatMessageVM
        {
            RoleLabel = "Error",
            Content = error,
            Accent = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)),
            Background = SystemBg
        });
        ScrollChatToBottom();
        StatusLabel.Text = $"Error: {error}";
    }

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        if (_messages.Count > 0)
            SaveCurrentChat();
        _messages.Clear();
        _messageImages.Clear();
        _chatMessages.Clear();
        ClearTokenProbabilityViewer();
        EmptyState.Visibility = Visibility.Visible;
        TokenCountLabel.Text = "";
        SpeedLabel.Text = "";
        _currentChatFile = null;
        _chatAttachedContext = null;
        _branchId = null;
        _parentChat = null;
        _branchPoint = null;
        _branchName = null;
        _regeneratingOriginal = null;
        _toolCallAccumulator = null;
        _pendingToolCalls.Clear();
        _pendingImages.Clear();
        ToolConfirmationPanel.Visibility = Visibility.Collapsed;
        RefreshImageAttachments();
        RefreshForkMessageOptions();
        UpdateChatContextLabel();
        StatusLabel.Text = "New chat started";
    }

    private static ChatMessageVM MakeChatVM(string role, string content)
    {
        return role switch
        {
            "user" => new ChatMessageVM
            {
                RoleLabel = "You",
                Content = content,
                Accent = UserAccent,
                Background = UserBg
            },
            "assistant" => new ChatMessageVM
            {
                RoleLabel = "Assistant",
                Content = content,
                Accent = AssistantAccent,
                Background = AssistantBg
            },
            "system" => new ChatMessageVM
            {
                RoleLabel = "System",
                Content = content,
                Accent = SystemAccent,
                Background = SystemBg
            },
            _ => new ChatMessageVM
            {
                RoleLabel = role,
                Content = content,
                Accent = UserAccent,
                Background = UserBg
            }
        };
    }

    private static string FormatChatContent(string content, IReadOnlyList<ChatImageAttachment> images)
    {
        if (images.Count == 0)
            return content;
        var names = string.Join(", ", images.Select(image => image.DisplayName));
        return $"{content}\n[Attached image{(images.Count == 1 ? "" : "s")}: {names}]";
    }

    private void ScrollChatToBottom()
    {
        if (VisualTreeHelper.GetChildrenCount(ChatScroll) > 0)
            ChatScroll.ScrollToEnd();
    }

    // ── Chat history persistence ─────────────────────────────────────────
    private void SaveCurrentChat()
    {
        if (_messages.Count == 0) return;

        if (_currentChatFile == null)
        {
            var firstUser = _messages.FirstOrDefault(m => m["role"] == "user")?["content"] ?? "chat";
            var slug = Regex.Replace(firstUser[..Math.Min(firstUser.Length, 50)], @"[^\w\s-]", "")
                .Trim().Replace(' ', '_');
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _currentChatFile = Path.Combine(_chatHistoryDir, $"{ts}_{slug}.json");
        }

        var json = ChatHistoryStore.Serialize(
            _messages,
            _chatAttachedContext,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            _branchId,
            _parentChat,
            _branchPoint,
            _branchName,
            _messageImages);
        File.WriteAllText(_currentChatFile, json);
        RefreshChatHistory();
    }

    private void RefreshChatHistory()
    {
        HistoryList.Items.Clear();
        if (!Directory.Exists(_chatHistoryDir)) return;

        var files = Directory.GetFiles(_chatHistoryDir, "*.json")
            .OrderByDescending(f => f)
            .Take(50);

        foreach (var fpath in files)
        {
            try
            {
                var json = File.ReadAllText(fpath);
                var document = ChatHistoryStore.Deserialize(json);
                string firstUser = Path.GetFileNameWithoutExtension(fpath);
                int msgCount = 0;

                foreach (var message in document.Messages)
                {
                    var role = message.Role;
                    if (role == "system") continue;
                    msgCount++;
                    if (role == "user" && firstUser == Path.GetFileNameWithoutExtension(fpath))
                    {
                        var c = message.Content;
                        firstUser = c.Length > 60 ? c[..60] : c;
                    }
                }

                var title = document.BranchName is { Length: > 0 } branchName
                    ? $"[Branch: {branchName}] {firstUser}"
                    : firstUser;
                var item = new ListBoxItem
                {
                    Content = $"{title}  [{msgCount} msgs]",
                    Tag = fpath
                };
                HistoryList.Items.Add(item);
            }
            catch { }
        }
    }

    private void History_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ListBoxItem item || item.Tag is not string fpath)
            return;

        try
        {
            var json = File.ReadAllText(fpath);
            var document = ChatHistoryStore.Deserialize(json);

            _messages.Clear();
            _messageImages.Clear();
            _chatMessages.Clear();
            ClearTokenProbabilityViewer();
            _pendingImages.Clear();
            _regeneratingOriginal = null;
            _toolCallAccumulator = null;
            _pendingToolCalls.Clear();
            ToolConfirmationPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;

            foreach (var (message, index) in document.Messages.Select((message, index) => (message, index)))
            {
                var role = message.Role;
                var content = message.Content;
                _messages.Add(new() { ["role"] = role, ["content"] = content });
                if (message.Images.Count > 0)
                    _messageImages[index] = VisionImageStore.CloneAll(message.Images);
                _chatMessages.Add(MakeChatVM(role, FormatChatContent(content, message.Images)));
            }

            _currentChatFile = fpath;
            _chatAttachedContext = document.ServerContext;
            _branchId = document.BranchId;
            _parentChat = document.ParentChat;
            _branchPoint = document.BranchPoint;
            _branchName = document.BranchName;
            var msgCount = _messages.Count(m => m["role"] != "system");
            TokenCountLabel.Text = $"{msgCount} messages";
            SpeedLabel.Text = "";
            StatusLabel.Text = $"Loaded chat: {Path.GetFileName(fpath)}";
            RefreshImageAttachments();
            RefreshForkMessageOptions();
            UpdateChatContextLabel();
            ScrollChatToBottom();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Failed to load chat: {ex.Message}";
        }
    }

    private void RefreshForkMessageOptions()
    {
        if (ForkMessageCombo is null || ForkBranchBtn is null || RegenerateBtn is null)
            return;

        var options = _messages
            .Select((message, index) =>
            {
                var role = message.TryGetValue("role", out var rawRole) ? rawRole : "message";
                var content = message.TryGetValue("content", out var rawContent) ? rawContent : "";
                var preview = Regex.Replace(content, @"\s+", " ").Trim();
                if (preview.Length > 64)
                    preview = preview[..64] + "...";
                return new BranchMessageOption
                {
                    Index = index,
                    Content = $"{index + 1}. {CultureInfo.CurrentCulture.TextInfo.ToTitleCase(role)}: {preview}",
                };
            })
            .ToList();

        ForkMessageCombo.ItemsSource = options;
        if (options.Count > 0)
            ForkMessageCombo.SelectedIndex = options.Count - 1;
        ForkBranchBtn.IsEnabled = options.Count > 0 && !_streaming && _pendingToolCalls.Count == 0;
        RegenerateBtn.IsEnabled = !_streaming
            && _pendingToolCalls.Count == 0
            && _messages.Count > 0
            && _messages[^1].TryGetValue("role", out var lastRole)
            && string.Equals(lastRole, "assistant", StringComparison.OrdinalIgnoreCase);
        UpdateBranchStatusLabel();
        RefreshFewShotEditor();
    }

    private void RefreshFewShotEditor()
    {
        if (FewShotTurnCombo is null || ApplyFewShotBtn is null || FewShotEditorBox is null)
            return;

        var history = _messages.Select(message => new ChatHistoryMessage
        {
            Role = message.TryGetValue("role", out var role) ? role : "",
            Content = message.TryGetValue("content", out var content) ? content : "",
        }).ToList();
        var previousIndex = (FewShotTurnCombo.SelectedItem as FewShotTurn)?.Index;
        var turns = ChatFewShotEditor.FindAssistantTurns(history);

        _updatingFewShot = true;
        FewShotTurnCombo.ItemsSource = turns;
        var selected = turns.FirstOrDefault(turn => turn.Index == previousIndex) ?? turns.LastOrDefault();
        FewShotTurnCombo.SelectedItem = selected;
        _updatingFewShot = false;

        FewShotEditorBox.Text = selected?.Content ?? "";
        ApplyFewShotBtn.IsEnabled = selected is not null && !_streaming && _pendingToolCalls.Count == 0;
        FewShotStatusLabel.Text = selected is null
            ? "Assistant turns appear here after a response."
            : $"{turns.Count} assistant turn{(turns.Count == 1 ? "" : "s")} available for editing.";
    }

    private void FewShotTurn_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFewShot)
            return;

        if (FewShotTurnCombo.SelectedItem is FewShotTurn turn)
        {
            FewShotEditorBox.Text = turn.Content;
            ApplyFewShotBtn.IsEnabled = !_streaming && _pendingToolCalls.Count == 0;
        }
        else
        {
            FewShotEditorBox.Clear();
            ApplyFewShotBtn.IsEnabled = false;
        }
    }

    private void ReloadFewShot_Click(object sender, RoutedEventArgs e)
    {
        RefreshFewShotEditor();
        FewShotStatusLabel.Text = FewShotTurnCombo.SelectedItem is null
            ? "Assistant turns appear here after a response."
            : "Loaded the selected assistant turn from chat history.";
    }

    private void ApplyFewShot_Click(object sender, RoutedEventArgs e)
    {
        if (_streaming || _pendingToolCalls.Count > 0)
        {
            FewShotStatusLabel.Text = "Finish the current response before editing a turn";
            return;
        }

        if (FewShotTurnCombo.SelectedItem is not FewShotTurn turn)
        {
            FewShotStatusLabel.Text = "Select an assistant turn to edit";
            return;
        }

        var history = _messages.Select(message => new ChatHistoryMessage
        {
            Role = message.TryGetValue("role", out var role) ? role : "",
            Content = message.TryGetValue("content", out var content) ? content : "",
        }).ToList();
        if (!ChatFewShotEditor.TryApplyAssistantEdit(history, turn.Index, FewShotEditorBox.Text))
        {
            FewShotStatusLabel.Text = "Assistant text cannot be empty";
            return;
        }

        _messages[turn.Index]["content"] = history[turn.Index].Content;
        if (turn.Index < _chatMessages.Count)
            _chatMessages[turn.Index] = MakeChatVM("assistant", history[turn.Index].Content);
        TokenCountLabel.Text = $"{_messages.Count(message => message["role"] != "system")} messages";
        SaveCurrentChat();
        RefreshFewShotEditor();
        FewShotStatusLabel.Text = $"Updated assistant turn {turn.Index + 1}; future sends will use the edited example.";
        ScrollChatToBottom();
    }

    private void UpdateBranchStatusLabel()
    {
        if (BranchStatusLabel is null)
            return;

        BranchStatusLabel.Text = string.IsNullOrWhiteSpace(_branchName)
            ? "Fork a message; the parent chat stays in History."
            : $"{ConversationBrancher.Describe(_branchName, _parentChat)}; parent retained in History.";
    }

    private string BuildUniqueBranchPath(string branchName)
    {
        var fileName = ConversationBrancher.BuildFileName(DateTimeOffset.Now, branchName);
        var candidate = Path.Combine(_chatHistoryDir, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        while (true)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            candidate = Path.Combine(_chatHistoryDir, $"{stem}_{suffix}.json");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private void ForkBranch_Click(object sender, RoutedEventArgs e)
    {
        if (_messages.Count == 0)
        {
            StatusLabel.Text = "Start a conversation before creating a branch";
            return;
        }

        if (_streaming || _pendingToolCalls.Count > 0)
        {
            StatusLabel.Text = "Finish the current response before creating a branch";
            return;
        }

        if (ForkMessageCombo.SelectedItem is not BranchMessageOption option)
        {
            StatusLabel.Text = "Choose a message to fork from";
            return;
        }

        try
        {
            SaveCurrentChat();
            var parentPath = _currentChatFile;
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                StatusLabel.Text = "The parent chat could not be saved";
                return;
            }

            var source = _messages.Select((message, index) => new ChatHistoryMessage
            {
                Role = message.TryGetValue("role", out var role) ? role : "",
                Content = message.TryGetValue("content", out var content) ? content : "",
                Images = _messageImages.TryGetValue(index, out var images)
                    ? VisionImageStore.CloneAll(images).ToList()
                    : new List<ChatImageAttachment>(),
            }).ToList();
            var branchMessages = ConversationBrancher.SliceThrough(source, option.Index);
            var branchName = Regex.Replace(BranchNameBox.Text.Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(branchName))
                branchName = "Alternate";
            if (branchName.Length > 60)
                branchName = branchName[..60].Trim();

            var branchPath = BuildUniqueBranchPath(branchName);
            _branchId = Guid.NewGuid().ToString("N");
            _parentChat = parentPath;
            _branchPoint = option.Index;
            _branchName = branchName;
            _currentChatFile = branchPath;

            _messages.Clear();
            _messageImages.Clear();
            _messages.AddRange(branchMessages.Select(message => new Dictionary<string, string>
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            }));
            _chatMessages.Clear();
            ClearTokenProbabilityViewer();
            foreach (var (message, index) in branchMessages.Select((message, index) => (message, index)))
            {
                if (message.Images.Count > 0)
                    _messageImages[index] = VisionImageStore.CloneAll(message.Images);
                _chatMessages.Add(MakeChatVM(message.Role, FormatChatContent(message.Content, message.Images)));
            }

            EmptyState.Visibility = _messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TokenCountLabel.Text = $"{_messages.Count(m => m["role"] != "system")} messages";
            BranchNameBox.Text = branchName;
            RefreshForkMessageOptions();
            UpdateChatContextLabel();
            SaveCurrentChat();

            var branchItem = HistoryList.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, branchPath, StringComparison.OrdinalIgnoreCase));
            if (branchItem is not null)
                HistoryList.SelectedItem = branchItem;

            StatusLabel.Text = $"{ConversationBrancher.Describe(branchName, parentPath)}; both chats remain in History";
            ScrollChatToBottom();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Failed to create branch: {ex.Message}";
        }
    }

    private void ExportChat_Click(object sender, RoutedEventArgs e)
    {
        if (_messages.Count == 0)
        {
            StatusLabel.Text = "No chat to export";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md|JSON (*.json)|*.json|Text (*.txt)|*.txt"
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var ext = Path.GetExtension(path).ToLower();
        var sb = new StringBuilder();

        if (ext == ".json")
        {
            File.WriteAllText(path, JsonSerializer.Serialize(
                new { messages = _messages }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var msg in _messages)
            {
                var role = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(msg["role"]);
                if (ext == ".md")
                    sb.AppendLine($"## {role}\n\n{msg["content"]}\n\n---\n");
                else
                    sb.AppendLine($"[{role}]\n{msg["content"]}\n");
            }
            File.WriteAllText(path, sb.ToString());
        }

        StatusLabel.Text = $"Chat exported to {path}";
    }

    private void DeleteChat_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ListBoxItem item || item.Tag is not string fpath)
            return;

        try
        {
            File.Delete(fpath);
            if (_currentChatFile == fpath)
            {
                _currentChatFile = null;
                _branchId = null;
                _parentChat = null;
                _branchPoint = null;
                _branchName = null;
                UpdateBranchStatusLabel();
            }
            RefreshChatHistory();
            StatusLabel.Text = "Chat deleted";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Failed to delete: {ex.Message}";
        }
    }

    // ── Generation parameter sliders ─────────────────────────────────────
    private void TempSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => TempLabel.Text = (TempSlider.Value / 100.0).ToString("F2");

    private void TopPSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => TopPLabel.Text = (TopPSlider.Value / 100.0).ToString("F2");

    private void RepSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => RepLabel.Text = (RepSlider.Value / 100.0).ToString("F2");

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not ComboBoxItem item) return;
        var name = item.Content?.ToString();

        var presets = new Dictionary<string, (int temp, int topP, int topK, int rep)>
        {
            ["Default"] = (70, 90, 40, 110),
            ["Creative"] = (120, 95, 80, 105),
            ["Precise"] = (20, 80, 20, 115),
            ["Code"] = (10, 85, 30, 100),
            ["Roleplay"] = (90, 92, 60, 108),
        };

        if (name != null && presets.TryGetValue(name, out var p))
        {
            TempSlider.Value = p.temp;
            TopPSlider.Value = p.topP;
            TopKBox.Text = p.topK.ToString();
            RepSlider.Value = p.rep;
        }
    }

    private GrammarConstraint GetGrammarConstraint()
    {
        var modeTag = (GrammarModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None";
        return new GrammarConstraint(GrammarTemplates.ParseMode(modeTag), GrammarEditorBox.Text.Trim());
    }

    private TokenProbabilityOptions GetTokenProbabilityOptions()
    {
        var topK = int.TryParse(TokenProbabilitiesKBox.Text, out var parsedTopK) ? parsedTopK : 5;
        return new TokenProbabilityOptions(TokenProbabilitiesCheck.IsChecked == true, topK);
    }

    private void ClearTokenProbabilityViewer()
    {
        _tokenProbabilityRows.Clear();
        TokenProbabilityStatusLabel.Text = _activeTokenProbabilityOptions.Enabled
            ? "Ready to collect token probabilities on the next response."
            : "Token probabilities are disabled.";
    }

    private void InspectPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var messages = BuildChatHistoryMessages();
            if (messages.Count == 0)
            {
                var systemPrompt = SystemPromptBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    messages.Add(new ChatHistoryMessage { Role = "system", Content = systemPrompt });
            }

            var draft = InputBox.Text.Trim();
            var images = VisionImageStore.CloneAll(_pendingImages).ToList();
            if (!string.IsNullOrWhiteSpace(draft) || images.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(draft) && images.Count > 0)
                    draft = "Describe the attached image.";
                messages.Add(new ChatHistoryMessage { Role = "user", Content = draft, Images = images });
            }

            var backend = GetSelectedBackend();
            var model = _serverManaged ? "" : ExtModelBox.Text.Trim();
            double temp = TempSlider.Value / 100.0;
            double topP = TopPSlider.Value / 100.0;
            double repPenalty = RepSlider.Value / 100.0;
            int.TryParse(TopKBox.Text, out var topK);
            int.TryParse(MaxTokensBox.Text, out var maxTokens);
            var payloadMessages = BuildPayloadMessagesWithRag(messages);
            var payload = BackendAdapter.BuildPayload(
                backend,
                model,
                payloadMessages,
                temp,
                topP,
                topK,
                repPenalty,
                maxTokens,
                tools: GetEnabledToolDefinitions(),
                grammar: GetGrammarConstraint(),
                tokenProbabilities: GetTokenProbabilityOptions());
            var endpoint = BackendAdapter.BuildEndpoint(GetServerUrl(), BackendAdapter.GetChatPath(backend));

            _lastPromptInspection = PromptInspector.Build(backend, endpoint, payloadMessages, payload);
            RenderPromptInspection();
            PromptInspectorStatusLabel.Text = $"Inspected {payloadMessages.Count} message(s) for {endpoint}.";
        }
        catch (Exception ex)
        {
            PromptInspectorStatusLabel.Text = $"Could not inspect prompt: {ex.Message}";
        }
    }

    private void PromptInspectorView_Changed(object sender, SelectionChangedEventArgs e)
        => RenderPromptInspection();

    private void RenderPromptInspection()
    {
        if (_lastPromptInspection is null
            || PromptInspectorBox is null
            || PromptInspectorTemplateLabel is null
            || PromptInspectorTokenLabel is null)
        {
            return;
        }

        var view = (PromptInspectorViewCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "payload";
        PromptInspectorBox.Text = view switch
        {
            "transcript" => _lastPromptInspection.Transcript,
            "tokens" => _lastPromptInspection.TokenPreview,
            _ => _lastPromptInspection.PayloadJson,
        };
        PromptInspectorTemplateLabel.Text =
            $"{_lastPromptInspection.TemplateDescription} Backend: {_lastPromptInspection.Backend}.";
        PromptInspectorTokenLabel.Text =
            $"Approximate prompt size: {_lastPromptInspection.EstimatedTokens} token(s); the exact count depends on the model tokenizer.";
    }

    private void GrammarMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GrammarEditorBox is null || GrammarStatusLabel is null) return;

        var mode = GrammarTemplates.ParseMode(
            (GrammarModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        if (mode != GrammarMode.Custom)
            GrammarEditorBox.Text = GrammarTemplates.GetTemplate(mode);
        GrammarStatusLabel.Text = GrammarTemplates.GetDescription(mode);
    }

    private void LoadGrammarTemplate_Click(object sender, RoutedEventArgs e)
        => GrammarMode_Changed(sender, null!);

    // ── HuggingFace model search & download ──────────────────────────────
    private void HfSearch_Click(object sender, RoutedEventArgs e) => HfSearch();
    private void HfSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) HfSearch();
    }

    private void HfSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(HfSearchBox.Text.Trim()))
            HfSearch();
    }

    private async void HfSearch()
    {
        var query = HfSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        HfSearchBtn.IsEnabled = false;
        HfSearchBtn.Content = "Searching...";
        HfModelGrid.ItemsSource = null;
        HfFilesGrid.ItemsSource = null;
        _hfCardCts?.Cancel();
        HfModelCardTitle.Text = "Select a model to view its README";
        HfModelCardMeta.Text = "";
        HfModelCardBox.Text = "";
        HfModelCardStatusLabel.Text = "";
        HfResultCount.Text = "";
        HfFilesLabel.Text = "Select a model above to see available GGUF files";

        var sortTag = (HfSortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "downloads";

        try
        {
            var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}" +
                      $"&filter=gguf&sort={sortTag}&direction=-1&limit=50";

            var headers = GetHfHeaders();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);

            var resp = await _http.SendAsync(request);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var results = new List<HfModelResult>();

            foreach (var m in doc.RootElement.EnumerateArray())
            {
                var id = m.GetProperty("id").GetString() ?? "";
                results.Add(new HfModelResult
                {
                    Id = id,
                    Name = id.Contains('/') ? id.Split('/').Last() : id,
                    Author = id.Contains('/') ? id.Split('/').First() : "",
                    Downloads = m.TryGetProperty("downloads", out var dl) ? dl.GetInt32() : 0,
                    Likes = m.TryGetProperty("likes", out var lk) ? lk.GetInt32() : 0,
                });
            }

            _hfCachedResults = results;
            HfModelGrid.ItemsSource = results;
            HfResultCount.Text = $"{results.Count} model(s) found";
            StatusLabel.Text = $"Found {results.Count} GGUF model(s) on HuggingFace";
        }
        catch (Exception ex)
        {
            HfResultCount.Text = "";
            StatusLabel.Text = $"Search failed: {ex.Message}";
        }

        HfSearchBtn.IsEnabled = true;
        HfSearchBtn.Content = "Search";
    }

    private async void HfModel_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (HfModelGrid.SelectedItem is not HfModelResult model) return;

        _hfSelectedRepo = model.Id;
        HfFilesGrid.ItemsSource = null;
        HfFilesLabel.Text = $"Loading files from {model.Id}...";
        DlStatusLabel.Text = "";
        _hfCardCts?.Cancel();
        _hfCardCts = new CancellationTokenSource();
        HfModelCardTitle.Text = model.Id;
        HfModelCardMeta.Text = "";
        HfModelCardBox.Text = "";
        HfModelCardStatusLabel.Text = "Loading README from Hugging Face...";
        _ = LoadHfModelCardAsync(model.Id, _hfCardCts.Token);

        try
        {
            var url = $"https://huggingface.co/api/models/{model.Id}";
            var headers = GetHfHeaders();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);

            var resp = await _http.SendAsync(request);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var siblings = doc.RootElement.GetProperty("siblings");
            var files = new List<HfFileResult>();

            foreach (var s in siblings.EnumerateArray())
            {
                var fname = s.GetProperty("rfilename").GetString() ?? "";
                if (!fname.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;

                long size = 0;
                if (s.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
                    size = sizeEl.GetInt64();
                if (size == 0 && s.TryGetProperty("lfs", out var lfs)
                    && lfs.TryGetProperty("size", out var lfsSize))
                    size = lfsSize.GetInt64();

                files.Add(new HfFileResult
                {
                    Filename = fname,
                    Size = size,
                    Quant = ParseQuant(fname)
                });
            }

            files.Sort((a, b) => a.Size.CompareTo(b.Size));
            HfFilesGrid.ItemsSource = files;

            HfFilesLabel.Text = files.Count > 0
                ? $"{model.Id}  -  {files.Count} GGUF file(s)"
                : $"No GGUF files found in {model.Id}";

            var dest = ModelFolderBox.Text.Trim();
            DlDestLabel.Text = !string.IsNullOrEmpty(dest)
                ? $"Downloads to: {dest}"
                : "Set model folder in sidebar to download";
        }
        catch (Exception ex)
        {
            HfFilesLabel.Text = "Error loading files";
            StatusLabel.Text = $"Failed to fetch files: {ex.Message}";
        }
    }

    private async Task LoadHfModelCardAsync(string repoId, CancellationToken cancellationToken)
    {
        try
        {
            var url = ModelCardParser.BuildRawReadmeUrl(repoId);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in GetHfHeaders())
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var markdown = await response.Content.ReadAsStringAsync(cancellationToken);
            var card = ModelCardParser.Parse(repoId, markdown);
            if (!string.Equals(_hfSelectedRepo, repoId, StringComparison.OrdinalIgnoreCase)) return;

            HfModelCardTitle.Text = card.Title;
            HfModelCardMeta.Text = string.IsNullOrWhiteSpace(card.Tags)
                ? $"License: {card.License}"
                : $"License: {card.License}  •  {card.Tags}";
            HfModelCardBox.Text = card.RenderedMarkdown;
            HfModelCardStatusLabel.Text =
                $"README loaded from {url} ({markdown.Length:N0} source characters).";
        }
        catch (OperationCanceledException)
        {
            // A newer model selection or window close superseded this request.
        }
        catch (Exception ex)
        {
            if (string.Equals(_hfSelectedRepo, repoId, StringComparison.OrdinalIgnoreCase))
            {
                HfModelCardBox.Text = "";
                HfModelCardStatusLabel.Text = $"README unavailable: {ex.Message}";
            }
        }
    }

    private async void HfDownload_Click(object sender, RoutedEventArgs e)
    {
        if (HfFilesGrid.SelectedItem is not HfFileResult file || string.IsNullOrEmpty(_hfSelectedRepo))
        {
            StatusLabel.Text = "Select a file to download";
            return;
        }

        var destFolder = ModelFolderBox.Text.Trim();
        if (string.IsNullOrEmpty(destFolder))
        {
            var dlg = new OpenFolderDialog { Title = "Select download folder" };
            if (dlg.ShowDialog() != true) return;
            destFolder = dlg.FolderName;
            ModelFolderBox.Text = destFolder;
        }

        var destPath = Path.Combine(destFolder, file.Filename);
        if (File.Exists(destPath))
        {
            var existingSize = new FileInfo(destPath).Length;
            if (file.Size > 0 && Math.Abs(existingSize - file.Size) < 1024)
            {
                DlStatusLabel.Text = $"Already downloaded: {file.Filename}";
                StatusLabel.Text = $"{file.Filename} already exists in model folder";
                return;
            }
        }

        DlBtn.Visibility = Visibility.Collapsed;
        DlCancelBtn.Visibility = Visibility.Visible;
        DlProgress.Visibility = Visibility.Visible;
        DlProgress.Value = 0;
        DlStatusLabel.Text = $"Downloading {file.Filename}...";
        var dlStartTime = Stopwatch.GetTimestamp();

        _downloadCts = new CancellationTokenSource();
        Directory.CreateDirectory(destFolder);

        var tempPath = destPath + ".part";
        long downloaded = 0;

        try
        {
            var headers = GetHfHeaders();
            if (File.Exists(tempPath))
            {
                downloaded = new FileInfo(tempPath).Length;
                headers["Range"] = $"bytes={downloaded}-";
            }

            var url = $"https://huggingface.co/{_hfSelectedRepo}/resolve/main/{file.Filename}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);

            var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);

            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (File.Exists(tempPath))
                {
                    File.Move(tempPath, destPath, true);
                    OnDownloadFinished(destPath, dlStartTime);
                    return;
                }
            }

            resp.EnsureSuccessStatusCode();
            var total = (resp.Content.Headers.ContentLength ?? 0) + downloaded;

            await using var stream = await resp.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var fs = new FileStream(tempPath, downloaded > 0 ? FileMode.Append : FileMode.Create);

            var buffer = new byte[1024 * 1024];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), _downloadCts.Token);
                downloaded += bytesRead;

                if (total > 0)
                {
                    var pct = (int)(downloaded * 100 / total);
                    DlProgress.Value = pct;
                    var elapsed = Stopwatch.GetElapsedTime(dlStartTime).TotalSeconds;
                    if (elapsed > 0.5)
                    {
                        var speed = downloaded / elapsed;
                        var speedStr = speed > 1024 * 1024
                            ? $"{speed / (1024 * 1024):F1} MB/s"
                            : $"{speed / 1024:F0} KB/s";
                        var remaining = speed > 0 ? (total - downloaded) / speed : 0;
                        var eta = remaining > 60
                            ? $"{remaining / 60:F0}m {remaining % 60:F0}s"
                            : $"{remaining:F0}s";
                        DlStatusLabel.Text = $"{downloaded / (1024.0 * 1024 * 1024):F2} / " +
                            $"{total / (1024.0 * 1024 * 1024):F2} GB  |  {speedStr}  |  ETA: {eta}";
                    }
                }
            }

            File.Move(tempPath, destPath, true);
            OnDownloadFinished(destPath, dlStartTime);
        }
        catch (OperationCanceledException)
        {
            DlBtn.Visibility = Visibility.Visible;
            DlCancelBtn.Visibility = Visibility.Collapsed;
            DlProgress.Visibility = Visibility.Collapsed;
            DlStatusLabel.Text = "Download cancelled (partial file kept for resume)";
            StatusLabel.Text = "Download cancelled";
        }
        catch (Exception ex)
        {
            DlBtn.Visibility = Visibility.Visible;
            DlCancelBtn.Visibility = Visibility.Collapsed;
            DlProgress.Visibility = Visibility.Collapsed;
            DlStatusLabel.Text = $"Download failed: {ex.Message}";
            StatusLabel.Text = ex.Message;
        }
    }

    private void OnDownloadFinished(string path, long startTimestamp)
    {
        DlBtn.Visibility = Visibility.Visible;
        DlCancelBtn.Visibility = Visibility.Collapsed;
        DlProgress.Visibility = Visibility.Collapsed;

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var sizeGb = new FileInfo(path).Length / (1024.0 * 1024 * 1024);
        DlStatusLabel.Text = $"Download complete: {Path.GetFileName(path)} ({sizeGb:F2} GB in {elapsed:F0}s)";
        StatusLabel.Text = $"Model downloaded: {Path.GetFileName(path)}";
        RefreshModels(ModelFolderBox.Text);
    }

    private void HfCancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private static string ParseQuant(string filename)
        => QuantRecommender.ParseQuant(filename);

    private static Dictionary<string, string> GetHfHeaders()
    {
        var headers = new Dictionary<string, string>();
        var token = Environment.GetEnvironmentVariable("HF_TOKEN")
                 ?? Environment.GetEnvironmentVariable("HUGGING_FACE_HUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            headers["Authorization"] = $"Bearer {token}";
        return headers;
    }

    // ── Settings persistence ─────────────────────────────────────────────
    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            // Auto-detect llama-server
            ExePathBox.Text = FindLlamaServer();
            ToolRootBox.Text = GetDefaultToolRoot();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string GetStr(string key, string def = "") =>
                root.TryGetProperty(key, out var v) ? v.GetString() ?? def : def;
            int GetInt(string key, int def) =>
                root.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;
            bool GetBool(string key, bool def) =>
                root.TryGetProperty(key, out var v) ? v.GetBoolean() : def;
            double GetDouble(string key, double def) =>
                root.TryGetProperty(key, out var v) && v.TryGetDouble(out var d) ? d : def;

            var exe = GetStr("exe_path");
            ExePathBox.Text = string.IsNullOrEmpty(exe) ? FindLlamaServer() : exe;
            ModelFolderBox.Text = GetStr("model_folder");
            PortBox.Text = GetInt("port", 8080).ToString();
            CtxBox.Text = GetInt("ctx_size", 4096).ToString();
            GpuBox.Text = GetInt("gpu_layers", 99).ToString();
            ThreadsBox.Text = GetInt("threads", Math.Max(1, Environment.ProcessorCount / 2)).ToString();
            VramBox.Text = GetDouble("vram_gb", 8).ToString("0.##", CultureInfo.InvariantCulture);
            RamBox.Text = GetDouble("ram_gb", 16).ToString("0.##", CultureInfo.InvariantCulture);
            TempSlider.Value = GetInt("temperature", 70);
            TopPSlider.Value = GetInt("top_p", 90);
            TopKBox.Text = GetInt("top_k", 40).ToString();
            RepSlider.Value = GetInt("repeat_penalty", 110);
            MaxTokensBox.Text = GetInt("max_tokens", 2048).ToString();
            TokenProbabilitiesCheck.IsChecked = GetBool("token_probabilities_enabled", false);
            TokenProbabilitiesKBox.Text = GetInt("token_probabilities_top_k", 5).ToString();
            var grammarMode = GrammarTemplates.ParseMode(GetStr("grammar_mode", "None"));
            GrammarModeCombo.SelectedItem = GrammarModeCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => GrammarTemplates.ParseMode(item.Tag?.ToString()) == grammarMode)
                ?? GrammarModeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
            var savedGrammar = GetStr("grammar_editor");
            GrammarEditorBox.Text = string.IsNullOrWhiteSpace(savedGrammar)
                ? GrammarTemplates.GetTemplate(grammarMode)
                : savedGrammar;
            GrammarStatusLabel.Text = GrammarTemplates.GetDescription(grammarMode);
            SystemPromptBox.Text = GetStr("system_prompt");
            FlashAttnCheck.IsChecked = GetBool("flash_attn", true);
            MlockCheck.IsChecked = GetBool("mlock", false);
            ExtUrlBox.Text = GetStr("ext_url", "http://127.0.0.1:8080");
            ExtModelBox.Text = GetStr("external_model", "llama3.2");
            ToolsEnabledCheck.IsChecked = GetBool("tools_enabled", false);
            FileReadToolCheck.IsChecked = GetBool("tool_read_file", true);
            CalculatorToolCheck.IsChecked = GetBool("tool_calculator", true);
            PythonToolCheck.IsChecked = GetBool("tool_python_eval", false);
            ToolRootBox.Text = GetStr("tool_root", GetDefaultToolRoot());
            WebSearchToolCheck.IsChecked = GetBool("tool_web_search", false);
            WebSearchEndpointBox.Text = GetStr("web_search_endpoint", "http://127.0.0.1:8080");
            var webSearchProvider = GetStr("web_search_provider", "duckduckgo");
            WebSearchProviderCombo.SelectedItem = WebSearchProviderCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(), webSearchProvider, StringComparison.OrdinalIgnoreCase))
                ?? WebSearchProviderCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
            SpeechFfmpegBox.Text = GetStr("speech_ffmpeg");
            SpeechWhisperBox.Text = GetStr("speech_whisper");
            SpeechWhisperModelBox.Text = GetStr("speech_whisper_model");
            SpeechPiperBox.Text = GetStr("speech_piper");
            SpeechPiperVoiceBox.Text = GetStr("speech_piper_voice");
            SpeechMicBox.Text = GetStr("speech_microphone", "default");
            ImageGenEnabledCheck.IsChecked = GetBool("image_generation_enabled", false);
            ImageGenExeBox.Text = GetStr("image_generation_exe");
            ImageGenModelBox.Text = GetStr("image_generation_model");
            ImageGenOutputBox.Text = GetStr("image_generation_output");
            ImageGenStepsBox.Text = GetInt("image_generation_steps", 20).ToString();
            ImageGenWidthBox.Text = GetInt("image_generation_width", 512).ToString();
            ImageGenHeightBox.Text = GetInt("image_generation_height", 512).ToString();
            RagEnabledCheck.IsChecked = GetBool("rag_enabled", false);
            RagTopKBox.Text = GetInt("rag_top_k", 4).ToString();
            RagFolderBox.Text = GetStr("rag_folder");
            _ragWatchOnStartup = GetBool("rag_watch_folder", false);
            var backendTag = GetStr("backend", "openai");
            BackendCombo.SelectedItem = BackendCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(), backendTag, StringComparison.OrdinalIgnoreCase))
                ?? BackendCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();

            var managed = GetBool("managed_mode", true);
            ManagedCheck.IsChecked = managed;

            _serverProfiles.Clear();
            _serverProfiles.AddRange(ServerProfileStore.Read(root));
            RefreshProfileCombo();
        }
        catch
        {
            ExePathBox.Text = FindLlamaServer();
            ToolRootBox.Text = GetDefaultToolRoot();
            _ragWatchOnStartup = false;
            _serverProfiles.Clear();
            RefreshProfileCombo();
        }
    }

    private static string GetDefaultToolRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void SaveSettings()
    {
        var settings = new Dictionary<string, object>
        {
            ["exe_path"] = ExePathBox.Text,
            ["model_folder"] = ModelFolderBox.Text,
            ["port"] = int.TryParse(PortBox.Text, out var p) ? p : 8080,
            ["ctx_size"] = int.TryParse(CtxBox.Text, out var c) ? c : 4096,
            ["gpu_layers"] = int.TryParse(GpuBox.Text, out var g) ? g : 99,
            ["threads"] = int.TryParse(ThreadsBox.Text, out var t) ? t : 4,
            ["vram_gb"] = TryParseCapacity(VramBox.Text, out var vram) ? vram : 8,
            ["ram_gb"] = TryParseCapacity(RamBox.Text, out var ram) ? ram : 16,
            ["temperature"] = (int)TempSlider.Value,
            ["top_p"] = (int)TopPSlider.Value,
            ["top_k"] = int.TryParse(TopKBox.Text, out var k) ? k : 40,
            ["repeat_penalty"] = (int)RepSlider.Value,
            ["max_tokens"] = int.TryParse(MaxTokensBox.Text, out var m) ? m : 2048,
            ["token_probabilities_enabled"] = TokenProbabilitiesCheck.IsChecked == true,
            ["token_probabilities_top_k"] = int.TryParse(TokenProbabilitiesKBox.Text, out var probabilityTopK)
                ? Math.Clamp(probabilityTopK, 1, 20)
                : 5,
            ["grammar_mode"] = (GrammarModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None",
            ["grammar_editor"] = GrammarEditorBox.Text,
            ["system_prompt"] = SystemPromptBox.Text,
            ["flash_attn"] = FlashAttnCheck.IsChecked == true,
            ["mlock"] = MlockCheck.IsChecked == true,
            ["ext_url"] = ExtUrlBox.Text,
            ["external_model"] = ExtModelBox.Text,
            ["backend"] = (BackendCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "openai",
            ["tools_enabled"] = ToolsEnabledCheck.IsChecked == true,
            ["tool_read_file"] = FileReadToolCheck.IsChecked == true,
            ["tool_calculator"] = CalculatorToolCheck.IsChecked == true,
            ["tool_python_eval"] = PythonToolCheck.IsChecked == true,
            ["tool_root"] = ToolRootBox.Text,
            ["tool_web_search"] = WebSearchToolCheck.IsChecked == true,
            ["web_search_provider"] = (WebSearchProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "duckduckgo",
            ["web_search_endpoint"] = WebSearchEndpointBox.Text,
            ["speech_ffmpeg"] = SpeechFfmpegBox.Text,
            ["speech_whisper"] = SpeechWhisperBox.Text,
            ["speech_whisper_model"] = SpeechWhisperModelBox.Text,
            ["speech_piper"] = SpeechPiperBox.Text,
            ["speech_piper_voice"] = SpeechPiperVoiceBox.Text,
            ["speech_microphone"] = SpeechMicBox.Text,
            ["image_generation_enabled"] = ImageGenEnabledCheck.IsChecked == true,
            ["image_generation_exe"] = ImageGenExeBox.Text,
            ["image_generation_model"] = ImageGenModelBox.Text,
            ["image_generation_output"] = ImageGenOutputBox.Text,
            ["image_generation_steps"] = int.TryParse(ImageGenStepsBox.Text, out var imageSteps) ? Math.Clamp(imageSteps, 1, 100) : 20,
            ["image_generation_width"] = int.TryParse(ImageGenWidthBox.Text, out var imageWidth) ? Math.Clamp(imageWidth, 128, 2048) : 512,
            ["image_generation_height"] = int.TryParse(ImageGenHeightBox.Text, out var imageHeight) ? Math.Clamp(imageHeight, 128, 2048) : 512,
            ["rag_enabled"] = RagEnabledCheck.IsChecked == true,
            ["rag_top_k"] = int.TryParse(RagTopKBox.Text, out var ragTopK) ? Math.Clamp(ragTopK, 1, 12) : 4,
            ["rag_folder"] = RagFolderBox.Text,
            ["rag_watch_folder"] = _ragWatcher is not null,
            ["managed_mode"] = ManagedCheck.IsChecked == true,
            ["selected_model"] = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
            ["server_profiles"] = ServerProfileStore.ToJson(_serverProfiles),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Window lifecycle ─────────────────────────────────────────────────
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        if (_messages.Count > 0)
            SaveCurrentChat();

        _healthTimer.Stop();
        _streamTimer.Stop();
        StopRagFolderWatch(report: false);
        _speechCts?.Cancel();
        _imageGenCts?.Cancel();
        if (_speechRecorder is not null && !_speechRecorder.HasExited)
        {
            try { _speechRecorder.Kill(true); } catch { }
        }
        _speechRecorder?.Dispose();
        _speechRecorder = null;
        _streamCts?.Cancel();
        _downloadCts?.Cancel();
        _serverUpdateCts?.Cancel();
        _hfCardCts?.Cancel();
        _hfCardCts?.Dispose();
        _hfCardCts = null;

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try { _serverProcess.Kill(true); } catch { }
            _serverProcess.WaitForExit(3000);
        }
    }
}
