using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Input;

namespace KittenMilkUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        private ObservableCollection<string> AllFiles { get; } = new ObservableCollection<string>();
        private string _scriptsFolderPath;
        private string _debugLogPath;
        private DispatcherTimer _logUpdateTimer;
        private string _lastLogContent = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _scriptsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            _debugLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "sirhurt", "sirhui", "sirh_debug_log.dat");

            Loaded += OnWindowLoaded;
            LoadScriptsList();
            scriptlists.SelectionChanged += ScriptsListBox_SelectionChanged;

            
            _logUpdateTimer = new DispatcherTimer();
            _logUpdateTimer.Interval = TimeSpan.FromSeconds(0.1);
            _logUpdateTimer.Tick += UpdateDebugLog;

            
            scriptlistsearch.GotFocus += (s, e) => {
                if (scriptlistsearch.Text == "Search")
                {
                    scriptlistsearch.Text = "";
                    scriptlistsearch.Foreground = System.Windows.Media.Brushes.White;
                }
            };

            scriptlistsearch.LostFocus += (s, e) => {
                if (string.IsNullOrWhiteSpace(scriptlistsearch.Text))
                {
                    scriptlistsearch.Text = "Search";
                    scriptlistsearch.Foreground = System.Windows.Media.Brushes.Gray;
                }
            };

            scriptlistsearch.TextChanged += (s, e) => {
                if (scriptlistsearch.Text == "Search") return;

                string searchText = scriptlistsearch.Text.ToLower();
                Files.Clear();

                foreach (string file in AllFiles)
                {
                    if (file.ToLower().Contains(searchText))
                    {
                        Files.Add(file);
                    }
                }
            };
        }

        private void UpdateDebugLog(object? sender, EventArgs e)
        {
            try
            {
                if (File.Exists(_debugLogPath))
                {
                    string logContent = File.ReadAllText(_debugLogPath);

                   
                    if (logContent != _lastLogContent)
                    {
                        _lastLogContent = logContent;
                        Dispatcher.Invoke(() =>
                        {
                            
                            var flowDocument = new FlowDocument();
                            var paragraph = new Paragraph();

                            
                            paragraph.Foreground = System.Windows.Media.Brushes.White;
                            paragraph.FontFamily = new System.Windows.Media.FontFamily("Consolas");

                            paragraph.Inlines.Add(new Run(logContent));
                            flowDocument.Blocks.Add(paragraph);
                            debuglog.Document = flowDocument;

                            
                            debuglog.ScrollToEnd();
                        });
                    }
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        var flowDocument = new FlowDocument();
                        flowDocument.Blocks.Add(new Paragraph(new Run("sirhurt debug file cannot be accsessed (maybe try running in admin)")));
                        debuglog.Document = flowDocument;
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var flowDocument = new FlowDocument();
                    flowDocument.Blocks.Add(new Paragraph(new Run($"error reading log: {ex.Message}")));
                    debuglog.Document = flowDocument;
                });
            }
        }

        private async void ScriptsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (scriptlists.SelectedItem == null) return;

            string selectedFile = scriptlists.SelectedItem.ToString();
            string filePath = Path.Combine(_scriptsFolderPath, selectedFile);

            if (File.Exists(filePath))
            {
                try
                {
                    string fileContent = await File.ReadAllTextAsync(filePath);
                    await LoadFileIntoEditor(selectedFile, fileContent, filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"not a valid file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadFileIntoEditor(string fileName, string content, string fullPath)
        {
            if (webView?.CoreWebView2 == null) return;

            string escapedContent = content
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            string script = $@"
                if (typeof OpenFileInNewTab === 'function') {{
                    OpenFileInNewTab('{fileName}', `{escapedContent}`, '{fullPath.Replace("\\", "\\\\")}');
                }} else {{
                    console.error('ace api error');
                }}";

            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void LoadScriptsList()
        {
            try
            {
                if (Directory.Exists(_scriptsFolderPath))
                {
                    Files.Clear();
                    AllFiles.Clear();
                    foreach (string file in Directory.GetFiles(_scriptsFolderPath))
                    {
                        if (IsSupportedFileType(file))
                        {
                            string fileName = Path.GetFileName(file);
                            Files.Add(fileName);
                            AllFiles.Add(fileName);
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(_scriptsFolderPath);
                    MessageBox.Show("scripts folder created.",
                        "Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"scripts folder is broken or not found: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool IsSupportedFileType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lua" || extension == ".txt" || extension == ".luau";
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewAsync();
            _logUpdateTimer.Start(); 
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

                string acePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ace");
                if (!Directory.Exists(acePath)) Directory.CreateDirectory(acePath);

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "ace",
                    acePath,
                    CoreWebView2HostResourceAccessKind.Allow);

                string html = await LoadHtmlContent();
                if (!string.IsNullOrEmpty(html))
                {
                    webView.NavigateToString(html);
                }

                await webView.CoreWebView2.ExecuteScriptAsync(@"
                    window.editorReady = new Promise(resolve => {
                        const checkReady = setInterval(() => {
                            if (typeof editor !== 'undefined' && editor !== null) {
                                clearInterval(checkReady);
                                resolve(true);
                            }
                        }, 100);
                    });");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 Error: {ex.Message}");
            }
        }

        private async Task<string> LoadHtmlContent()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "KittenMilkUI.Resources.editor.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    MessageBox.Show("editor not found");
                    return string.Empty;
                }

                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"editor failed to load: {ex.Message}");
                return string.Empty;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _logUpdateTimer.Stop(); 
            base.OnClosed(e);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

        }
    } 
}