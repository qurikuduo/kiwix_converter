using KiwixConverter.Core.Models;
using KiwixConverter.Core.Services;

namespace KiwixConverter.WinForms;

public sealed class MainForm : Form
{
    private readonly KiwixAppService _appService = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };

    private readonly TextBox _kiwixDirectoryTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _defaultOutputDirectoryTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _zimdumpPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _snapshotIntervalUpDown = new() { Minimum = 5, Maximum = 3600, Value = 15, Dock = DockStyle.Fill };
    private readonly TextBox _taskOutputOverrideTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _historySearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Search by path, status, output directory or error..." };
    private readonly TextBox _logSearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Search logs by message, category or article URL..." };

    private readonly DataGridView _downloadsGrid = CreateGrid();
    private readonly DataGridView _tasksGrid = CreateGrid();
    private readonly DataGridView _historyGrid = CreateGrid();
    private readonly DataGridView _logsGrid = CreateGrid();

    private readonly Button _saveSettingsButton = new() { Text = "Save Settings", AutoSize = true };
    private readonly Button _scanButton = new() { Text = "Scan ZIM Files", AutoSize = true };
    private readonly Button _convertButton = new() { Text = "Convert Selected ZIM", AutoSize = true };
    private readonly Button _pauseTaskButton = new() { Text = "Pause Selected Task", AutoSize = true };
    private readonly Button _resumeTaskButton = new() { Text = "Resume Selected Task", AutoSize = true };
    private readonly Button _refreshHistoryButton = new() { Text = "Refresh History", AutoSize = true };
    private readonly Button _refreshLogsButton = new() { Text = "Refresh Logs", AutoSize = true };

    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _selectedTaskLogsOnlyCheckBox = new() { Text = "Selected task only", AutoSize = true };

    private readonly HashSet<long> _knownCompletedTaskIds = [];

    private bool _allowClose;
    private bool _isRefreshing;

    public MainForm()
    {
        Text = "Kiwix Converter";
        Width = 1500;
        Height = 920;
        MinimumSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();

        _notifyIcon.Visible = true;
        _notifyIcon.Icon = SystemIcons.Information;
        _notifyIcon.Text = "Kiwix Converter";
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await InitializeAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildLayout()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520,
            Panel1MinSize = 420,
            Panel2MinSize = 600
        };

        root.Panel1.Controls.Add(BuildLeftPanel());
        root.Panel2.Controls.Add(BuildRightPanel());

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(root);
        Controls.Add(statusStrip);
    }

    private Control BuildLeftPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildSettingsGroup(), 0, 0);
        layout.Controls.Add(BuildConversionGroup(), 0, 1);
        layout.Controls.Add(BuildDownloadsGroup(), 0, 2);
        return layout;
    }

    private Control BuildSettingsGroup()
    {
        var group = new GroupBox
        {
            Text = "Directories And Tooling",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 5,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddLabeledRow(table, 0, "kiwix-desktop", _kiwixDirectoryTextBox, CreateButton("Browse...", (_, _) => BrowseForFolder(_kiwixDirectoryTextBox)));
        AddLabeledRow(table, 1, "Default Output", _defaultOutputDirectoryTextBox, CreateButton("Browse...", (_, _) => BrowseForFolder(_defaultOutputDirectoryTextBox)));
        AddLabeledRow(table, 2, "zimdump Path", _zimdumpPathTextBox, CreateButton("Browse...", (_, _) => BrowseForExecutable()));
        AddLabeledRow(table, 3, "Snapshot Seconds", _snapshotIntervalUpDown, new Label { Text = "Article-level checkpoints + periodic task snapshots", AutoSize = true, Anchor = AnchorStyles.Left });

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(3, 12, 3, 3)
        };
        buttonPanel.Controls.Add(_saveSettingsButton);
        buttonPanel.Controls.Add(_scanButton);
        table.Controls.Add(buttonPanel, 1, 4);
        table.SetColumnSpan(buttonPanel, 2);

        group.Controls.Add(table);
        return group;
    }

    private Control BuildConversionGroup()
    {
        var group = new GroupBox
        {
            Text = "Per-Task Output Override",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddLabeledRow(table, 0, "Output Override", _taskOutputOverrideTextBox, CreateButton("Browse...", (_, _) => BrowseForFolder(_taskOutputOverrideTextBox)));

        var helperLabel = new Label
        {
            Text = "Leave empty to use the default output directory. Each ZIM is exported into its own subdirectory.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(420, 0)
        };
        table.Controls.Add(helperLabel, 1, 1);
        table.Controls.Add(_convertButton, 2, 1);

        group.Controls.Add(table);
        return group;
    }

    private Control BuildDownloadsGroup()
    {
        var group = new GroupBox
        {
            Text = "Downloaded ZIM Files",
            Dock = DockStyle.Fill
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "Completed items are marked directly in this list. Select one row and start a conversion from the override panel above.",
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(470, 0)
        }, 0, 0);
        layout.Controls.Add(_downloadsGrid, 0, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildRightPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTasksTab());
        tabs.TabPages.Add(BuildHistoryTab());
        tabs.TabPages.Add(BuildLogsTab());
        return tabs;
    }

    private TabPage BuildTasksTab()
    {
        var tab = new TabPage("Tasks");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false
        };
        buttonPanel.Controls.Add(_pauseTaskButton);
        buttonPanel.Controls.Add(_resumeTaskButton);
        buttonPanel.Controls.Add(new Label
        {
            Text = "Running tasks auto-save progress before exit and resume from article checkpoints.",
            AutoSize = true,
            Margin = new Padding(16, 8, 3, 3)
        });

        layout.Controls.Add(buttonPanel, 0, 0);
        layout.Controls.Add(_tasksGrid, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildHistoryTab()
    {
        var tab = new TabPage("History");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true
        };
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        filterPanel.Controls.Add(new Label { Text = "Keyword", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        filterPanel.Controls.Add(_historySearchTextBox, 1, 0);
        filterPanel.Controls.Add(_refreshHistoryButton, 2, 0);

        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(_historyGrid, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildLogsTab()
    {
        var tab = new TabPage("Logs");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 5,
            RowCount = 1,
            AutoSize = true
        };
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        filterPanel.Controls.Add(new Label { Text = "Search", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        filterPanel.Controls.Add(_logSearchTextBox, 1, 0);
        filterPanel.Controls.Add(_selectedTaskLogsOnlyCheckBox, 2, 0);
        filterPanel.Controls.Add(_refreshLogsButton, 3, 0);
        filterPanel.Controls.Add(new Label { Text = "Select a task row to filter logs by task ID.", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) }, 4, 0);

        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(_logsGrid, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private void WireEvents()
    {
        _saveSettingsButton.Click += async (_, _) => await SaveSettingsAsync();
        _scanButton.Click += async (_, _) => await ScanAndRefreshAsync();
        _convertButton.Click += async (_, _) => await ConvertSelectedAsync();
        _pauseTaskButton.Click += async (_, _) => await PauseSelectedTaskAsync();
        _resumeTaskButton.Click += async (_, _) => await ResumeSelectedTaskAsync();
        _refreshHistoryButton.Click += async (_, _) => await RefreshAllViewsAsync();
        _refreshLogsButton.Click += async (_, _) => await RefreshAllViewsAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshAllViewsAsync();
        FormClosing += OnMainFormClosingAsync;
    }

    private async Task InitializeAsync()
    {
        try
        {
            SetStatus("Initializing application state...");
            await _appService.InitializeAsync();
            await LoadSettingsIntoFormAsync();
            if (!await EnsureRequiredDirectoriesConfiguredAsync())
            {
                return;
            }

            await ScanAndRefreshAsync(initialLoad: true);
            _refreshTimer.Start();
            SetStatus("Ready.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task LoadSettingsIntoFormAsync()
    {
        var settings = await _appService.GetSettingsAsync();
        _kiwixDirectoryTextBox.Text = settings.KiwixDesktopDirectory ?? string.Empty;
        _defaultOutputDirectoryTextBox.Text = settings.DefaultOutputDirectory ?? string.Empty;
        _zimdumpPathTextBox.Text = settings.ZimdumpExecutablePath ?? string.Empty;
        _snapshotIntervalUpDown.Value = Math.Min(_snapshotIntervalUpDown.Maximum, Math.Max(_snapshotIntervalUpDown.Minimum, settings.SnapshotIntervalSeconds));
    }

    private async Task<bool> EnsureRequiredDirectoriesConfiguredAsync()
    {
        if (!Directory.Exists(_kiwixDirectoryTextBox.Text))
        {
            var selected = PromptForFolder("Select the kiwix-desktop directory. This is required before the application can continue.");
            if (string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(this, "A kiwix-desktop directory is required. The application will close.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return false;
            }

            _kiwixDirectoryTextBox.Text = selected;
        }

        if (!Directory.Exists(_defaultOutputDirectoryTextBox.Text))
        {
            var selected = PromptForFolder("Select the default output directory. This is required before the application can continue.");
            if (string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(this, "A default output directory is required. The application will close.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return false;
            }

            _defaultOutputDirectoryTextBox.Text = selected;
        }

        await SaveSettingsAsync(silent: true);
        return true;
    }

    private async Task SaveSettingsAsync(bool silent = false)
    {
        var settings = new AppSettings
        {
            KiwixDesktopDirectory = string.IsNullOrWhiteSpace(_kiwixDirectoryTextBox.Text) ? null : _kiwixDirectoryTextBox.Text.Trim(),
            DefaultOutputDirectory = string.IsNullOrWhiteSpace(_defaultOutputDirectoryTextBox.Text) ? null : _defaultOutputDirectoryTextBox.Text.Trim(),
            ZimdumpExecutablePath = string.IsNullOrWhiteSpace(_zimdumpPathTextBox.Text) ? null : _zimdumpPathTextBox.Text.Trim(),
            SnapshotIntervalSeconds = (int)_snapshotIntervalUpDown.Value
        };

        await _appService.SaveSettingsAsync(settings);
        if (!silent)
        {
            SetStatus("Settings saved.");
        }
    }

    private async Task ScanAndRefreshAsync(bool initialLoad = false)
    {
        await SaveSettingsAsync(silent: true);
        SetStatus("Scanning the configured kiwix-desktop directory...");
        await _appService.ScanAsync();
        await RefreshAllViewsAsync(initialLoad);
        SetStatus("ZIM scan completed.");
    }

    private async Task RefreshAllViewsAsync(bool initialLoad = false)
    {
        if (_isRefreshing || IsDisposed)
        {
            return;
        }

        try
        {
            _isRefreshing = true;

            var downloads = await _appService.GetDownloadsAsync();
            var tasks = await _appService.GetTasksAsync();
            var history = await _appService.GetTasksAsync(string.IsNullOrWhiteSpace(_historySearchTextBox.Text) ? null : _historySearchTextBox.Text.Trim());
            var logs = await _appService.GetLogsAsync(
                string.IsNullOrWhiteSpace(_logSearchTextBox.Text) ? null : _logSearchTextBox.Text.Trim(),
                _selectedTaskLogsOnlyCheckBox.Checked ? GetSelectedTaskId() : null,
                500);

            _downloadsGrid.DataSource = downloads.Select(static item => new DownloadRow
            {
                Id = item.Id,
                Completed = item.IsConverted,
                FileName = item.DisplayName,
                Language = item.Language ?? string.Empty,
                Publisher = item.Publisher ?? string.Empty,
                SizeMb = Math.Round(item.SizeBytes / 1024d / 1024d, 2),
                LastWriteLocal = item.LastWriteUtc.ToLocalTime(),
                Path = item.FullPath
            }).ToList();

            _tasksGrid.DataSource = tasks.Select(static task => new TaskRow
            {
                Id = task.Id,
                ZimFile = Path.GetFileName(task.ZimPath),
                Status = task.Status.ToString(),
                Progress = task.TotalArticles > 0 ? $"{task.ProcessedArticles}/{task.TotalArticles}" : task.ProcessedArticles.ToString(),
                Skipped = task.SkippedArticles,
                CurrentArticle = task.CurrentArticleUrl ?? string.Empty,
                StartedLocal = task.StartedUtc?.ToLocalTime(),
                CompletedLocal = task.CompletedUtc?.ToLocalTime(),
                OutputDirectory = task.OutputDirectory,
                Error = task.ErrorMessage ?? string.Empty
            }).ToList();

            _historyGrid.DataSource = history.Select(static task => new TaskRow
            {
                Id = task.Id,
                ZimFile = Path.GetFileName(task.ZimPath),
                Status = task.Status.ToString(),
                Progress = task.TotalArticles > 0 ? $"{task.ProcessedArticles}/{task.TotalArticles}" : task.ProcessedArticles.ToString(),
                Skipped = task.SkippedArticles,
                CurrentArticle = task.CurrentArticleUrl ?? string.Empty,
                StartedLocal = task.StartedUtc?.ToLocalTime(),
                CompletedLocal = task.CompletedUtc?.ToLocalTime(),
                OutputDirectory = task.OutputDirectory,
                Error = task.ErrorMessage ?? string.Empty
            }).ToList();

            _logsGrid.DataSource = logs.Select(static log => new LogRow
            {
                Id = log.Id,
                TaskId = log.TaskId,
                TimeLocal = log.TimestampUtc.ToLocalTime(),
                Level = log.Level.ToString(),
                Category = log.Category,
                Message = log.Message,
                ArticleUrl = log.ArticleUrl ?? string.Empty,
                Details = log.Details ?? string.Empty
            }).ToList();

            if (initialLoad)
            {
                foreach (var task in tasks.Where(static task => task.Status == ConversionTaskStatus.Completed))
                {
                    _knownCompletedTaskIds.Add(task.Id);
                }
            }
            else
            {
                NotifyNewCompletedTasks(tasks);
            }
        }
        catch (Exception exception)
        {
            SetStatus("Refresh failed.");
            MessageBox.Show(this, exception.Message, "Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void NotifyNewCompletedTasks(IReadOnlyList<ConversionTaskRecord> tasks)
    {
        foreach (var task in tasks.Where(static task => task.Status == ConversionTaskStatus.Completed))
        {
            if (!_knownCompletedTaskIds.Add(task.Id))
            {
                continue;
            }

            _notifyIcon.ShowBalloonTip(
                5000,
                "Conversion Complete",
                $"{Path.GetFileName(task.ZimPath)} finished exporting to {task.OutputDirectory}",
                ToolTipIcon.Info);
        }
    }

    private async Task ConvertSelectedAsync()
    {
        var downloadId = GetSelectedDownloadId();
        if (downloadId is null)
        {
            MessageBox.Show(this, "Select a downloaded ZIM file first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus("Starting conversion task...");
        await SaveSettingsAsync(silent: true);
        await _appService.StartOrResumeTaskAsync(downloadId.Value, string.IsNullOrWhiteSpace(_taskOutputOverrideTextBox.Text) ? null : _taskOutputOverrideTextBox.Text.Trim());
        await RefreshAllViewsAsync();
        SetStatus("Conversion task started or resumed.");
    }

    private async Task PauseSelectedTaskAsync()
    {
        var taskId = GetSelectedTaskId();
        if (taskId is null)
        {
            MessageBox.Show(this, "Select a task row first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus("Pausing selected task...");
        await _appService.PauseTaskAsync(taskId.Value);
        await RefreshAllViewsAsync();
        SetStatus("Task paused.");
    }

    private async Task ResumeSelectedTaskAsync()
    {
        var taskId = GetSelectedTaskId();
        if (taskId is null)
        {
            MessageBox.Show(this, "Select a task row first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus("Resuming selected task...");
        await _appService.ResumeTaskAsync(taskId.Value);
        await RefreshAllViewsAsync();
        SetStatus("Task resumed.");
    }

    private async void OnMainFormClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _refreshTimer.Stop();
        SetStatus("Saving running task state before exit...");

        try
        {
            await _appService.PauseAllAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Pause Before Exit Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _allowClose = true;
        Close();
    }

    private long? GetSelectedDownloadId()
    {
        return _downloadsGrid.CurrentRow?.DataBoundItem is DownloadRow row ? row.Id : null;
    }

    private long? GetSelectedTaskId()
    {
        if (_tasksGrid.CurrentRow?.DataBoundItem is TaskRow taskRow)
        {
            return taskRow.Id;
        }

        return _historyGrid.CurrentRow?.DataBoundItem is TaskRow historyRow ? historyRow.Id : null;
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private static void AddLabeledRow(TableLayoutPanel table, int rowIndex, string label, Control control, Control accessory)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, rowIndex);
        table.Controls.Add(control, 1, rowIndex);
        table.Controls.Add(accessory, 2, rowIndex);
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += onClick;
        return button;
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false
        };
    }

    private void BrowseForFolder(TextBox target)
    {
        var selected = PromptForFolder("Select a folder.");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            target.Text = selected;
        }
    }

    private string? PromptForFolder(string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_defaultOutputDirectoryTextBox.Text) ? _defaultOutputDirectoryTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void BrowseForExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Executable|*.exe|All files|*.*",
            Title = "Select zimdump executable"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _zimdumpPathTextBox.Text = dialog.FileName;
        }
    }

    private sealed class DownloadRow
    {
        public long Id { get; init; }

        public bool Completed { get; init; }

        public string FileName { get; init; } = string.Empty;

        public string Language { get; init; } = string.Empty;

        public string Publisher { get; init; } = string.Empty;

        public double SizeMb { get; init; }

        public DateTime LastWriteLocal { get; init; }

        public string Path { get; init; } = string.Empty;
    }

    private sealed class TaskRow
    {
        public long Id { get; init; }

        public string ZimFile { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Progress { get; init; } = string.Empty;

        public int Skipped { get; init; }

        public string CurrentArticle { get; init; } = string.Empty;

        public DateTime? StartedLocal { get; init; }

        public DateTime? CompletedLocal { get; init; }

        public string OutputDirectory { get; init; } = string.Empty;

        public string Error { get; init; } = string.Empty;
    }

    private sealed class LogRow
    {
        public long Id { get; init; }

        public long? TaskId { get; init; }

        public DateTime TimeLocal { get; init; }

        public string Level { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string ArticleUrl { get; init; } = string.Empty;

        public string Details { get; init; } = string.Empty;
    }
}