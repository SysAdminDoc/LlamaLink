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
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _streamTimer;
    private readonly DispatcherTimer _healthTimer;
    private readonly string _settingsPath;
    private readonly string _chatHistoryDir;

    private Process? _serverProcess;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _downloadCts;
    private bool _streaming;
    private string _streamBuffer = "";
    private bool _streamDirty;
    private int _tokenCount;
    private long _streamStartTime;
    private string? _currentChatFile;
    private bool _serverManaged;
    private string? _hfSelectedRepo;
    private List<HfModelResult> _hfCachedResults = new();
    private Thread? _serverThread;

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

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "settings.json");
        _chatHistoryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".llamalink", "chats");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        Directory.CreateDirectory(_chatHistoryDir);

        ChatMessages.ItemsSource = _chatMessages;

        _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _streamTimer.Tick += (_, _) => FlushStream();

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _healthTimer.Tick += async (_, _) => await CheckServerHealth();

        LoadSettings();
        RefreshChatHistory();

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
        bool managed = ManagedCheck.IsChecked == true;
        ExeRow.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ExtUrlRow.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        PortRow.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        StartBtn.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ConnectBtn.Visibility = managed ? Visibility.Collapsed : Visibility.Visible;
        ModelGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
        ServerParamsGroup.Visibility = managed ? Visibility.Visible : Visibility.Collapsed;
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
        }
        else
        {
            ModelInfoLabel.Text = "";
        }
    }

    // ── Server management ────────────────────────────────────────────────
    private string GetServerUrl()
    {
        if (_serverManaged)
            return $"http://127.0.0.1:{PortBox.Text.Trim()}";
        return ExtUrlBox.Text.Trim().TrimEnd('/');
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

        ServerStatusLabel.Text = "Connecting...";
        ServerDot.Fill = FindResource("YellowBrush") as SolidColorBrush;
        StatusLabel.Text = $"Connecting to {url}...";

        try
        {
            var resp = await _http.GetAsync($"{url}/health", new CancellationTokenSource(5000).Token);
            if (resp.IsSuccessStatusCode)
            {
                _serverManaged = false;
                ConnectBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;
                OnServerReady();
                _healthTimer.Start();
                AppendServerLog($"Connected to external server: {url}");
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
    }

    private async Task CheckServerHealth()
    {
        if (_serverManaged) return;
        try
        {
            var resp = await _http.GetAsync($"{GetServerUrl()}/health",
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
    private void Send_Click(object sender, RoutedEventArgs e) => SendMessage();
    private void StopGen_Click(object sender, RoutedEventArgs e) => _streamCts?.Cancel();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            SendMessage();
        }
    }

    private async void SendMessage()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || _streaming)
            return;

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
        _chatMessages.Add(MakeChatVM("user", text));
        EmptyState.Visibility = Visibility.Collapsed;
        ScrollChatToBottom();

        double temp = TempSlider.Value / 100.0;
        double topP = TopPSlider.Value / 100.0;
        double repPenalty = RepSlider.Value / 100.0;
        int.TryParse(TopKBox.Text, out int topK);
        int.TryParse(MaxTokensBox.Text, out int maxTokens);

        var payload = new Dictionary<string, object>
        {
            ["messages"] = _messages.Select(m => new { role = m["role"], content = m["content"] }).ToArray(),
            ["stream"] = true,
            ["temperature"] = temp,
            ["top_p"] = topP,
            ["top_k"] = topK,
            ["repeat_penalty"] = repPenalty
        };
        if (maxTokens > 0)
            payload["max_tokens"] = maxTokens;

        _streaming = true;
        _streamBuffer = "";
        _streamDirty = false;
        _tokenCount = 0;
        _streamStartTime = Stopwatch.GetTimestamp();
        SendBtn.Visibility = Visibility.Collapsed;
        StopGenBtn.Visibility = Visibility.Visible;
        SpeedLabel.Text = "";

        // Add placeholder assistant message
        var assistantVM = MakeChatVM("assistant", "Thinking...");
        _chatMessages.Add(assistantVM);
        ScrollChatToBottom();

        _streamCts = new CancellationTokenSource();
        _streamTimer.Start();
        StatusLabel.Text = "Generating response...";

        try
        {
            var url = $"{GetServerUrl()}/v1/chat/completions";
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
                if (!line.StartsWith("data: ")) continue;

                var data = line[6..];
                if (data.Trim() == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var delta = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("delta");
                    if (delta.TryGetProperty("content", out var tokenEl))
                    {
                        var token = tokenEl.GetString();
                        if (!string.IsNullOrEmpty(token))
                        {
                            _streamBuffer += token;
                            _tokenCount++;
                            _streamDirty = true;
                        }
                    }
                }
                catch { }
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
        StopGenBtn.Visibility = Visibility.Collapsed;

        var elapsed = Stopwatch.GetElapsedTime(_streamStartTime).TotalSeconds;
        if (elapsed > 0 && _tokenCount > 0)
        {
            var tps = _tokenCount / elapsed;
            SpeedLabel.Text = $"{tps:F1} tok/s ({_tokenCount} tokens in {elapsed:F1}s)";
        }

        if (!string.IsNullOrEmpty(_streamBuffer))
        {
            _messages.Add(new() { ["role"] = "assistant", ["content"] = _streamBuffer });

            // Final update of assistant bubble
            if (_chatMessages.Count > 0)
            {
                _chatMessages[^1] = new ChatMessageVM
                {
                    RoleLabel = "Assistant",
                    Content = _streamBuffer,
                    Accent = AssistantAccent,
                    Background = AssistantBg
                };
            }
        }

        var msgCount = _messages.Count(m => m["role"] != "system");
        TokenCountLabel.Text = $"{msgCount} messages";
        StatusLabel.Text = "Response complete";
        ScrollChatToBottom();

        SaveCurrentChat();
    }

    private void OnChatError(string error)
    {
        _streamTimer.Stop();
        _streaming = false;
        SendBtn.Visibility = Visibility.Visible;
        StopGenBtn.Visibility = Visibility.Collapsed;
        SpeedLabel.Text = "";

        // Remove the placeholder assistant message
        if (_chatMessages.Count > 0 && _chatMessages[^1].RoleLabel == "Assistant")
            _chatMessages.RemoveAt(_chatMessages.Count - 1);

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
        _chatMessages.Clear();
        EmptyState.Visibility = Visibility.Visible;
        TokenCountLabel.Text = "";
        SpeedLabel.Text = "";
        _currentChatFile = null;
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

        var data = new { messages = _messages, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        File.WriteAllText(_currentChatFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
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
                using var doc = JsonDocument.Parse(json);
                var msgs = doc.RootElement.GetProperty("messages");
                string firstUser = Path.GetFileNameWithoutExtension(fpath);
                int msgCount = 0;

                foreach (var m in msgs.EnumerateArray())
                {
                    var role = m.GetProperty("role").GetString();
                    if (role == "system") continue;
                    msgCount++;
                    if (role == "user" && firstUser == Path.GetFileNameWithoutExtension(fpath))
                    {
                        var c = m.GetProperty("content").GetString() ?? "";
                        firstUser = c.Length > 60 ? c[..60] : c;
                    }
                }

                var item = new ListBoxItem
                {
                    Content = $"{firstUser}  [{msgCount} msgs]",
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
            using var doc = JsonDocument.Parse(json);
            var msgs = doc.RootElement.GetProperty("messages");

            _messages.Clear();
            _chatMessages.Clear();
            EmptyState.Visibility = Visibility.Collapsed;

            foreach (var m in msgs.EnumerateArray())
            {
                var role = m.GetProperty("role").GetString()!;
                var content = m.GetProperty("content").GetString()!;
                _messages.Add(new() { ["role"] = role, ["content"] = content });
                _chatMessages.Add(MakeChatVM(role, content));
            }

            _currentChatFile = fpath;
            var msgCount = _messages.Count(m => m["role"] != "system");
            TokenCountLabel.Text = $"{msgCount} messages";
            SpeedLabel.Text = "";
            StatusLabel.Text = $"Loaded chat: {Path.GetFileName(fpath)}";
            ScrollChatToBottom();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Failed to load chat: {ex.Message}";
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
                _currentChatFile = null;
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
    {
        var m = Regex.Match(filename, @"[.\-]((?:I?Q\d[\w_]*?))[.\-]", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpper();
        m = Regex.Match(filename, @"[.\-]((?:I?Q\d[\w_]*))\.", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpper();
        m = Regex.Match(filename, @"((?:I?Q\d[\w_]*))", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpper() : "";
    }

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

            var exe = GetStr("exe_path");
            ExePathBox.Text = string.IsNullOrEmpty(exe) ? FindLlamaServer() : exe;
            ModelFolderBox.Text = GetStr("model_folder");
            PortBox.Text = GetInt("port", 8080).ToString();
            CtxBox.Text = GetInt("ctx_size", 4096).ToString();
            GpuBox.Text = GetInt("gpu_layers", 99).ToString();
            ThreadsBox.Text = GetInt("threads", Math.Max(1, Environment.ProcessorCount / 2)).ToString();
            TempSlider.Value = GetInt("temperature", 70);
            TopPSlider.Value = GetInt("top_p", 90);
            TopKBox.Text = GetInt("top_k", 40).ToString();
            RepSlider.Value = GetInt("repeat_penalty", 110);
            MaxTokensBox.Text = GetInt("max_tokens", 2048).ToString();
            SystemPromptBox.Text = GetStr("system_prompt");
            FlashAttnCheck.IsChecked = GetBool("flash_attn", true);
            MlockCheck.IsChecked = GetBool("mlock", false);
            ExtUrlBox.Text = GetStr("ext_url", "http://127.0.0.1:8080");

            var managed = GetBool("managed_mode", true);
            ManagedCheck.IsChecked = managed;
        }
        catch
        {
            ExePathBox.Text = FindLlamaServer();
        }
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
            ["temperature"] = (int)TempSlider.Value,
            ["top_p"] = (int)TopPSlider.Value,
            ["top_k"] = int.TryParse(TopKBox.Text, out var k) ? k : 40,
            ["repeat_penalty"] = (int)RepSlider.Value,
            ["max_tokens"] = int.TryParse(MaxTokensBox.Text, out var m) ? m : 2048,
            ["system_prompt"] = SystemPromptBox.Text,
            ["flash_attn"] = FlashAttnCheck.IsChecked == true,
            ["mlock"] = MlockCheck.IsChecked == true,
            ["ext_url"] = ExtUrlBox.Text,
            ["managed_mode"] = ManagedCheck.IsChecked == true,
            ["selected_model"] = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
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
        _streamCts?.Cancel();
        _downloadCts?.Cancel();

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try { _serverProcess.Kill(true); } catch { }
            _serverProcess.WaitForExit(3000);
        }
    }
}
