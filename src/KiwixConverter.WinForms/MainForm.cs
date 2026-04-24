using System.Globalization;
using System.Reflection;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;
using KiwixConverter.Core.Services;

namespace KiwixConverter.WinForms;

public sealed partial class MainForm : Form
{
    private const int DefaultWindowWidth = 1760;
    private const int DefaultWindowHeight = 920;
    private const int DefaultRootSplitterDistance = 680;

    private static readonly UiLocalizer UiText = UiLocalizer.CreateForCurrentCulture();

    private readonly KiwixAppService _appService = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };
    private readonly Image? _brandLogo = LoadBrandLogo();

    private readonly TextBox _kiwixDirectoryTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _defaultOutputDirectoryTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _zimdumpPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _snapshotIntervalUpDown = new() { Minimum = 5, Maximum = 3600, Value = 15, Dock = DockStyle.Fill };
    private readonly TextBox _taskOutputOverrideTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _historySearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = T("Search by path, status, output directory or error...") };
    private readonly TextBox _logSearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = T("Search logs by message, category or article URL...") };
    private readonly SplitContainer _rootSplitContainer = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical
    };

    private readonly DataGridView _downloadsGrid = CreateGrid();
    private readonly DataGridView _tasksGrid = CreateGrid();
    private readonly DataGridView _historyGrid = CreateGrid();
    private readonly DataGridView _logsGrid = CreateGrid();

    private readonly Button _saveSettingsButton = new() { Text = T("Save Settings"), AutoSize = true };
    private readonly Button _scanButton = new() { Text = T("Scan ZIM Files"), AutoSize = true };
    private readonly Button _convertButton = new() { Text = T("Convert Selected ZIM"), AutoSize = true };
    private readonly Button _pauseTaskButton = new() { Text = T("Pause Selected Task"), AutoSize = true };
    private readonly Button _resumeTaskButton = new() { Text = T("Resume Selected Task"), AutoSize = true };
    private readonly Button _refreshHistoryButton = new() { Text = T("Refresh History"), AutoSize = true };
    private readonly Button _refreshLogsButton = new() { Text = T("Refresh Logs"), AutoSize = true };

    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _selectedTaskLogsOnlyCheckBox = new() { Text = T("Selected task only"), AutoSize = true };

    private readonly HashSet<long> _knownCompletedTaskIds = [];

    private bool _allowClose;
    private bool _isRefreshing;
    private int _preferredRootSplitterDistance = DefaultRootSplitterDistance;

    public MainForm()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), ".ctor", new
        {
            applicationDataDirectory = FileTraceLogger.SummarizePath(AppPaths.ApplicationDataDirectory),
            logFile = FileTraceLogger.SummarizePath(FileTraceLogger.CurrentLogFilePath)
        });

        try
        {
            Text = BuildWindowTitle();
            Width = DefaultWindowWidth;
            Height = DefaultWindowHeight;
            MinimumSize = new Size(1460, 760);
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = UiText.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = UiText.IsRightToLeft;

            InitializeWeKnoraControls();
            FileTraceLogger.Info(nameof(MainForm), "Constructor STEP", new { step = nameof(InitializeWeKnoraControls) });

            ApplyBranding();
            BuildLayout();
            WireEvents();
            FileTraceLogger.Info(nameof(MainForm), "Constructor STEP", new { step = nameof(WireEvents) });

            _notifyIcon.Visible = true;
            _notifyIcon.Text = T("Kiwix Converter");

            scope.Success(new
            {
                text = Text,
                width = Width,
                height = Height
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(OnShown), new
        {
            isHandleCreated = IsHandleCreated,
            visible = Visible
        });

        try
        {
            base.OnShown(e);
            EnsureRootSplitterDistance();
            await InitializeAsync();
            scope.Success(new
            {
                splitterDistance = _rootSplitContainer.SplitterDistance,
                refreshTimerEnabled = _refreshTimer.Enabled
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(OnFormClosed), new
        {
            closeReason = e.CloseReason.ToString()
        });

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _refreshTimer.Dispose();
            _brandLogo?.Dispose();
            base.OnFormClosed(e);
            scope.Success();
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private void BuildLayout()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(BuildLayout));

        try
        {
            _rootSplitContainer.Panel1.Controls.Add(BuildLeftPanel());
            _rootSplitContainer.Panel2.Controls.Add(BuildRightPanel());

            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(_statusLabel);

            Controls.Add(_rootSplitContainer);
            Controls.Add(statusStrip);

            scope.Success(new
            {
                rootControls = Controls.Count,
                leftPanelControls = _rootSplitContainer.Panel1.Controls.Count,
                rightPanelControls = _rootSplitContainer.Panel2.Controls.Count
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private static string T(string englishText)
    {
        return UiText.Get(englishText);
    }

    private static string TF(string englishFormat, params object[] args)
    {
        return UiText.Format(englishFormat, args);
    }

    private void ApplyBranding()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(ApplyBranding), new
        {
            executablePath = FileTraceLogger.SummarizePath(Application.ExecutablePath)
        });

        try
        {
            using var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extractedIcon is not null)
            {
                Icon = (Icon)extractedIcon.Clone();
                _notifyIcon.Icon = (Icon)extractedIcon.Clone();
                scope.Success(new { iconSource = "Application.ExecutablePath" });
                return;
            }
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { fallbackIcon = "SystemIcons.Information" });
            _notifyIcon.Icon = SystemIcons.Information;
            return;
        }

        _notifyIcon.Icon = SystemIcons.Information;
        scope.Success(new { iconSource = "SystemIcons.Information" });
    }

    private void EnsureRootSplitterDistance()
    {
        const int desiredPanel1MinSize = 420;
        const int desiredPanel2MinSize = 600;

        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(EnsureRootSplitterDistance), new
        {
            width = _rootSplitContainer.Width,
            panel1MinSize = desiredPanel1MinSize,
            panel2MinSize = desiredPanel2MinSize
        });

        _rootSplitContainer.Panel1MinSize = desiredPanel1MinSize;
        _rootSplitContainer.Panel2MinSize = desiredPanel2MinSize;

        var maxSplitterDistance = _rootSplitContainer.Width - _rootSplitContainer.Panel2MinSize;
        if (maxSplitterDistance < _rootSplitContainer.Panel1MinSize)
        {
            scope.Success(new
            {
                skipped = true,
                maxSplitterDistance
            });
            return;
        }

        var desiredSplitterDistance = Math.Max(
            _rootSplitContainer.Panel1MinSize,
            Math.Min(_preferredRootSplitterDistance, maxSplitterDistance));
        if (_rootSplitContainer.SplitterDistance != desiredSplitterDistance)
        {
            _rootSplitContainer.SplitterDistance = desiredSplitterDistance;
        }

        scope.Success(new
        {
            skipped = false,
            maxSplitterDistance,
            splitterDistance = _rootSplitContainer.SplitterDistance
        });
    }

    private Control BuildLeftPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildBrandHeader(), 0, 0);
        layout.Controls.Add(BuildSettingsGroup(), 0, 1);
        layout.Controls.Add(BuildWeKnoraSettingsGroup(), 0, 2);
        layout.Controls.Add(BuildConversionGroup(), 0, 3);
        layout.Controls.Add(BuildDownloadsGroup(), 0, 4);
        return layout;
    }

    private Control BuildBrandHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 128,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 6),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(244, 249, 250)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brandImage = CreateBrandSymbolImage();
        if (brandImage is not null)
        {
            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 0),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = brandImage
            };
            layout.Controls.Add(pictureBox, 0, 0);
            layout.SetRowSpan(pictureBox, 3);
        }

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0),
            Text = T("Kiwix Converter"),
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 57, 70)
        }, 1, 0);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0),
            Text = T("ZIM to Markdown, RAG artifacts, and WeKnora sync"),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(47, 87, 99)
        }, 1, 1);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            Text = TF("Version {0}", GetDisplayVersion()),
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(103, 128, 138)
        }, 1, 2);

        panel.Controls.Add(layout);

        return panel;
    }

    private Image? CreateBrandSymbolImage()
    {
        if (Icon is not null)
        {
            return Icon.ToBitmap();
        }

        if (_brandLogo is null)
        {
            return null;
        }

        var cropSize = Math.Min(_brandLogo.Width, _brandLogo.Height);
        var bitmap = new Bitmap(cropSize, cropSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(
            _brandLogo,
            new Rectangle(0, 0, cropSize, cropSize),
            new Rectangle(0, 0, cropSize, cropSize),
            GraphicsUnit.Pixel);
        return bitmap;
    }

    private static string BuildWindowTitle()
    {
        var version = GetDisplayVersion();
        return string.IsNullOrWhiteSpace(version)
            ? T("Kiwix Converter")
            : TF("Kiwix Converter v{0}", version);
    }

    private static string GetDisplayVersion()
    {
        var version = Application.ProductVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var separatorIndex = version.IndexOf('+');
        return (separatorIndex >= 0 ? version[..separatorIndex] : version).Trim();
    }

    private static Image? LoadBrandLogo()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(LoadBrandLogo), new
        {
            resourceName = "KiwixConverter.WinForms.Resources.AppLogo.png"
        });

        try
        {
            using var resourceStream = typeof(MainForm).Assembly.GetManifestResourceStream("KiwixConverter.WinForms.Resources.AppLogo.png");
            if (resourceStream is null)
            {
                scope.Success(new { loaded = false });
                return null;
            }

            using var buffer = new MemoryStream();
            resourceStream.CopyTo(buffer);
            buffer.Position = 0;
            using var image = Image.FromStream(buffer);
            var bitmap = new Bitmap(image);
            scope.Success(new
            {
                loaded = true,
                width = bitmap.Width,
                height = bitmap.Height
            });
            return bitmap;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private Control BuildSettingsGroup()
    {
        var group = new GroupBox
        {
            Text = T("Directories And Tooling"),
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 8,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddStackedInputRow(table, 0, "kiwix-desktop", _kiwixDirectoryTextBox, CreateButton(T("Browse..."), (_, _) => BrowseForFolder(_kiwixDirectoryTextBox)));
        AddStackedInputRow(table, 2, T("Default Output"), _defaultOutputDirectoryTextBox, CreateButton(T("Browse..."), (_, _) => BrowseForFolder(_defaultOutputDirectoryTextBox)));
        AddStackedInputRow(table, 4, T("zimdump Path"), _zimdumpPathTextBox, CreateButton(T("Browse..."), (_, _) => BrowseForExecutable()));
        AddLabeledRow(table, 6, T("Snapshot Seconds"), _snapshotIntervalUpDown, new Label { Text = T("Article-level checkpoints + periodic task snapshots"), AutoSize = true, Anchor = AnchorStyles.Left });

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
        table.Controls.Add(buttonPanel, 1, 7);
        table.SetColumnSpan(buttonPanel, 2);

        group.Controls.Add(table);
        return group;
    }

    private Control BuildConversionGroup()
    {
        var group = new GroupBox
        {
            Text = T("Per-Task Output Override"),
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddStackedInputRow(table, 0, T("Output Override"), _taskOutputOverrideTextBox, CreateButton(T("Browse..."), (_, _) => BrowseForFolder(_taskOutputOverrideTextBox)));

        var helperLabel = new Label
        {
            Text = T("Leave empty to use the default output directory. Each ZIM is exported into its own subdirectory."),
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(420, 0)
        };
        table.Controls.Add(helperLabel, 1, 2);
        table.Controls.Add(_convertButton, 2, 2);

        group.Controls.Add(table);
        return group;
    }

    private Control BuildDownloadsGroup()
    {
        var group = new GroupBox
        {
            Text = T("Downloaded ZIM Files"),
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
            Text = T("Completed items are marked directly in this list. Select one row and start a conversion from the override panel above."),
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
        tabs.TabPages.Add(BuildWeKnoraSyncTab());
        return tabs;
    }

    private TabPage BuildTasksTab()
    {
        var tab = new TabPage(T("Tasks"));
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
            Text = T("Running tasks auto-save progress before exit and resume from article checkpoints."),
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
        var tab = new TabPage(T("History"));
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

        filterPanel.Controls.Add(new Label { Text = T("Keyword"), Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        filterPanel.Controls.Add(_historySearchTextBox, 1, 0);
        filterPanel.Controls.Add(_refreshHistoryButton, 2, 0);

        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(_historyGrid, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildLogsTab()
    {
        var tab = new TabPage(T("Logs"));
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

        filterPanel.Controls.Add(new Label { Text = T("Search"), Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        filterPanel.Controls.Add(_logSearchTextBox, 1, 0);
        filterPanel.Controls.Add(_selectedTaskLogsOnlyCheckBox, 2, 0);
        filterPanel.Controls.Add(_refreshLogsButton, 3, 0);
        filterPanel.Controls.Add(new Label { Text = T("Select a task row to filter logs by task ID."), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) }, 4, 0);

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
        WireWeKnoraEvents();
    }

    private async Task InitializeAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(InitializeAsync), new
        {
            logFile = FileTraceLogger.SummarizePath(FileTraceLogger.CurrentLogFilePath)
        });

        try
        {
            SetStatus(T("Initializing application state..."));
            await _appService.InitializeAsync();
            FileTraceLogger.Info(nameof(MainForm), "InitializeAsync STEP", new { step = "AppService.InitializeAsync completed" });
            await LoadSettingsIntoFormAsync();
            if (!await EnsureRequiredDirectoriesConfiguredAsync())
            {
                scope.Success(new
                {
                    ready = false,
                    reason = "Required directories missing"
                });
                return;
            }

            await EnsureZimdumpAvailableAsync();

            await ScanAndRefreshAsync(initialLoad: true);
            await TryLoadWeKnoraKnowledgeBasesAsync();
            _refreshTimer.Start();
            SetStatus(T("Ready."));
            scope.Success(new
            {
                ready = true,
                refreshTimerEnabled = _refreshTimer.Enabled
            });
        }
        catch (Exception exception)
        {
            var startupLogPath = Path.Combine(AppPaths.ApplicationDataDirectory, "startup-error.log");
            File.WriteAllText(startupLogPath, exception.ToString());
            SetStatus(TF("Initialization failed. See {0} for details.", startupLogPath));
            scope.Fail(exception, new
            {
                startupErrorLog = FileTraceLogger.SummarizePath(startupLogPath)
            });
        }
    }

    private async Task LoadSettingsIntoFormAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(LoadSettingsIntoFormAsync));

        try
        {
            var settings = await _appService.GetSettingsAsync();
            _kiwixDirectoryTextBox.Text = settings.KiwixDesktopDirectory ?? string.Empty;
            _defaultOutputDirectoryTextBox.Text = settings.DefaultOutputDirectory ?? string.Empty;
            _zimdumpPathTextBox.Text = settings.ZimdumpExecutablePath ?? string.Empty;
            _taskOutputOverrideTextBox.Text = settings.TaskOutputOverrideDirectory ?? string.Empty;
            _snapshotIntervalUpDown.Value = Math.Min(_snapshotIntervalUpDown.Maximum, Math.Max(_snapshotIntervalUpDown.Minimum, settings.SnapshotIntervalSeconds));
            _weKnoraBaseUrlTextBox.Text = settings.WeKnoraBaseUrl ?? string.Empty;
            _weKnoraAccessTokenTextBox.Text = settings.WeKnoraAccessToken ?? string.Empty;
            _weKnoraKnowledgeBaseIdTextBox.Text = settings.WeKnoraKnowledgeBaseId ?? string.Empty;
            _weKnoraKnowledgeBaseNameTextBox.Text = settings.WeKnoraKnowledgeBaseName ?? string.Empty;
            _weKnoraKnowledgeBaseDescriptionTextBox.Text = settings.WeKnoraKnowledgeBaseDescription ?? string.Empty;
            _weKnoraChatModelIdComboBox.Text = settings.WeKnoraChatModelId ?? string.Empty;
            _weKnoraEmbeddingModelIdComboBox.Text = settings.WeKnoraEmbeddingModelId ?? string.Empty;
            _weKnoraMultimodalModelIdComboBox.Text = settings.WeKnoraMultimodalModelId ?? string.Empty;
            _weKnoraChunkSizeUpDown.Value = Math.Max(_weKnoraChunkSizeUpDown.Minimum, Math.Min(_weKnoraChunkSizeUpDown.Maximum, settings.WeKnoraChunkSize));
            _weKnoraChunkOverlapUpDown.Value = Math.Max(_weKnoraChunkOverlapUpDown.Minimum, Math.Min(_weKnoraChunkOverlapUpDown.Maximum, settings.WeKnoraChunkOverlap));
            _weKnoraEnableParentChildCheckBox.Checked = settings.WeKnoraEnableParentChild;
            _weKnoraAuthModeComboBox.SelectedItem = settings.WeKnoraAuthMode.ToString();
            _weKnoraAutoCreateKnowledgeBaseCheckBox.Checked = settings.WeKnoraAutoCreateKnowledgeBase;
            _weKnoraAppendMetadataCheckBox.Checked = settings.WeKnoraAppendMetadataBlock;
            _historySearchTextBox.Text = settings.HistorySearchText ?? string.Empty;
            _logSearchTextBox.Text = settings.LogSearchText ?? string.Empty;
            _selectedTaskLogsOnlyCheckBox.Checked = settings.SelectedTaskLogsOnly;
            _weKnoraSyncSearchTextBox.Text = settings.WeKnoraSyncSearchText ?? string.Empty;
            _weKnoraSyncLogSearchTextBox.Text = settings.WeKnoraSyncLogSearchText ?? string.Empty;
            _selectedWeKnoraSyncLogsOnlyCheckBox.Checked = settings.SelectedWeKnoraSyncLogsOnly;
            ApplyStoredLayoutPreferences(settings);

            scope.Success(SummarizeAppSettings(settings));
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private async Task<bool> EnsureRequiredDirectoriesConfiguredAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(EnsureRequiredDirectoriesConfiguredAsync), new
        {
            kiwixDirectory = FileTraceLogger.SummarizePath(_kiwixDirectoryTextBox.Text),
            defaultOutputDirectory = FileTraceLogger.SummarizePath(_defaultOutputDirectoryTextBox.Text)
        });

        var missingDirectories = new List<string>();

        if (!Directory.Exists(_kiwixDirectoryTextBox.Text))
        {
            missingDirectories.Add(T("the kiwix-desktop directory"));
        }

        if (!Directory.Exists(_defaultOutputDirectoryTextBox.Text))
        {
            missingDirectories.Add(T("the default output directory"));
        }

        if (missingDirectories.Count > 0)
        {
            SetStatus(TF("Configure {0} before scanning or converting.", string.Join(T(" and "), missingDirectories)));
            scope.Success(new
            {
                configured = false,
                missingDirectories
            });
            return false;
        }

        await SaveSettingsAsync(silent: true);
        scope.Success(new { configured = true });
        return true;
    }

    private async Task SaveSettingsAsync(bool silent = false)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(SaveSettingsAsync), new { silent });

        var persistedWindowSize = GetPersistedWindowSize();

        var settings = new AppSettings
        {
            KiwixDesktopDirectory = string.IsNullOrWhiteSpace(_kiwixDirectoryTextBox.Text) ? null : _kiwixDirectoryTextBox.Text.Trim(),
            DefaultOutputDirectory = string.IsNullOrWhiteSpace(_defaultOutputDirectoryTextBox.Text) ? null : _defaultOutputDirectoryTextBox.Text.Trim(),
            ZimdumpExecutablePath = string.IsNullOrWhiteSpace(_zimdumpPathTextBox.Text) ? null : _zimdumpPathTextBox.Text.Trim(),
            TaskOutputOverrideDirectory = string.IsNullOrWhiteSpace(_taskOutputOverrideTextBox.Text) ? null : _taskOutputOverrideTextBox.Text.Trim(),
            SnapshotIntervalSeconds = (int)_snapshotIntervalUpDown.Value,
            WeKnoraBaseUrl = string.IsNullOrWhiteSpace(_weKnoraBaseUrlTextBox.Text) ? null : _weKnoraBaseUrlTextBox.Text.Trim(),
            WeKnoraAccessToken = string.IsNullOrWhiteSpace(_weKnoraAccessTokenTextBox.Text) ? null : _weKnoraAccessTokenTextBox.Text.Trim(),
            WeKnoraKnowledgeBaseId = string.IsNullOrWhiteSpace(_weKnoraKnowledgeBaseIdTextBox.Text) ? null : _weKnoraKnowledgeBaseIdTextBox.Text.Trim(),
            WeKnoraKnowledgeBaseName = string.IsNullOrWhiteSpace(_weKnoraKnowledgeBaseNameTextBox.Text) ? null : _weKnoraKnowledgeBaseNameTextBox.Text.Trim(),
            WeKnoraKnowledgeBaseDescription = string.IsNullOrWhiteSpace(_weKnoraKnowledgeBaseDescriptionTextBox.Text) ? null : _weKnoraKnowledgeBaseDescriptionTextBox.Text.Trim(),
            WeKnoraChatModelId = GetWeKnoraModelSelection(_weKnoraChatModelIdComboBox),
            WeKnoraEmbeddingModelId = GetWeKnoraModelSelection(_weKnoraEmbeddingModelIdComboBox),
            WeKnoraMultimodalModelId = GetWeKnoraModelSelection(_weKnoraMultimodalModelIdComboBox),
            WeKnoraChunkSize = (int)_weKnoraChunkSizeUpDown.Value,
            WeKnoraChunkOverlap = (int)_weKnoraChunkOverlapUpDown.Value,
            WeKnoraEnableParentChild = _weKnoraEnableParentChildCheckBox.Checked,
            WeKnoraAuthMode = Enum.TryParse<WeKnoraAuthMode>(_weKnoraAuthModeComboBox.SelectedItem?.ToString(), out var authMode) ? authMode : WeKnoraAuthMode.ApiKey,
            WeKnoraAutoCreateKnowledgeBase = _weKnoraAutoCreateKnowledgeBaseCheckBox.Checked,
            WeKnoraAppendMetadataBlock = _weKnoraAppendMetadataCheckBox.Checked,
            HistorySearchText = string.IsNullOrWhiteSpace(_historySearchTextBox.Text) ? null : _historySearchTextBox.Text.Trim(),
            LogSearchText = string.IsNullOrWhiteSpace(_logSearchTextBox.Text) ? null : _logSearchTextBox.Text.Trim(),
            SelectedTaskLogsOnly = _selectedTaskLogsOnlyCheckBox.Checked,
            WeKnoraSyncSearchText = string.IsNullOrWhiteSpace(_weKnoraSyncSearchTextBox.Text) ? null : _weKnoraSyncSearchTextBox.Text.Trim(),
            WeKnoraSyncLogSearchText = string.IsNullOrWhiteSpace(_weKnoraSyncLogSearchTextBox.Text) ? null : _weKnoraSyncLogSearchTextBox.Text.Trim(),
            SelectedWeKnoraSyncLogsOnly = _selectedWeKnoraSyncLogsOnlyCheckBox.Checked,
            MainWindowWidth = Math.Max(MinimumSize.Width, persistedWindowSize.Width),
            MainWindowHeight = Math.Max(MinimumSize.Height, persistedWindowSize.Height),
            RootSplitterDistance = _rootSplitContainer.SplitterDistance > 0 ? _rootSplitContainer.SplitterDistance : DefaultRootSplitterDistance,
            WeKnoraSyncUpperSplitterDistance = _weKnoraSyncUpperSplitContainer.SplitterDistance > 0 ? _weKnoraSyncUpperSplitContainer.SplitterDistance : DefaultWeKnoraSyncUpperSplitterDistance
        };

        try
        {
            await _appService.SaveSettingsAsync(settings);
            if (!silent)
            {
                SetStatus(T("Settings saved."));
            }

            scope.Success(SummarizeAppSettings(settings));
        }
        catch (Exception exception)
        {
            scope.Fail(exception, CaptureCurrentSettingsInput());
            throw;
        }
    }

    private object CaptureCurrentSettingsInput()
    {
        return new
        {
            kiwixDirectory = FileTraceLogger.SummarizePath(_kiwixDirectoryTextBox.Text),
            defaultOutputDirectory = FileTraceLogger.SummarizePath(_defaultOutputDirectoryTextBox.Text),
            zimdumpExecutablePath = FileTraceLogger.SummarizePath(_zimdumpPathTextBox.Text),
            taskOutputOverride = FileTraceLogger.SummarizePath(_taskOutputOverrideTextBox.Text),
            historySearchText = FileTraceLogger.SummarizeText(_historySearchTextBox.Text, 240),
            logSearchText = FileTraceLogger.SummarizeText(_logSearchTextBox.Text, 240),
            selectedTaskLogsOnly = _selectedTaskLogsOnlyCheckBox.Checked,
            snapshotIntervalSeconds = (int)_snapshotIntervalUpDown.Value,
            weKnoraBaseUrl = FileTraceLogger.SummarizeText(_weKnoraBaseUrlTextBox.Text, 240),
            weKnoraAccessToken = FileTraceLogger.RedactSecret(_weKnoraAccessTokenTextBox.Text),
            weKnoraKnowledgeBaseId = FileTraceLogger.SummarizeText(_weKnoraKnowledgeBaseIdTextBox.Text),
            weKnoraKnowledgeBaseName = FileTraceLogger.SummarizeText(_weKnoraKnowledgeBaseNameTextBox.Text),
            weKnoraKnowledgeBaseDescription = FileTraceLogger.SummarizeText(_weKnoraKnowledgeBaseDescriptionTextBox.Text, 240),
            weKnoraChatModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraChatModelIdComboBox)),
            weKnoraEmbeddingModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraEmbeddingModelIdComboBox)),
            weKnoraMultimodalModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraMultimodalModelIdComboBox)),
            weKnoraChunkSize = (int)_weKnoraChunkSizeUpDown.Value,
            weKnoraChunkOverlap = (int)_weKnoraChunkOverlapUpDown.Value,
            weKnoraEnableParentChild = _weKnoraEnableParentChildCheckBox.Checked,
            weKnoraAuthMode = _weKnoraAuthModeComboBox.SelectedItem?.ToString(),
            weKnoraAutoCreateKnowledgeBase = _weKnoraAutoCreateKnowledgeBaseCheckBox.Checked,
            weKnoraAppendMetadataBlock = _weKnoraAppendMetadataCheckBox.Checked,
            weKnoraSyncSearchText = FileTraceLogger.SummarizeText(_weKnoraSyncSearchTextBox.Text, 240),
            weKnoraSyncLogSearchText = FileTraceLogger.SummarizeText(_weKnoraSyncLogSearchTextBox.Text, 240),
            selectedWeKnoraSyncLogsOnly = _selectedWeKnoraSyncLogsOnlyCheckBox.Checked,
            mainWindowWidth = Width,
            mainWindowHeight = Height,
            rootSplitterDistance = _rootSplitContainer.SplitterDistance,
            weKnoraSyncUpperSplitterDistance = _weKnoraSyncUpperSplitContainer.SplitterDistance
        };
    }

    private static object SummarizeAppSettings(AppSettings settings)
    {
        return new
        {
            kiwixDirectory = FileTraceLogger.SummarizePath(settings.KiwixDesktopDirectory),
            defaultOutputDirectory = FileTraceLogger.SummarizePath(settings.DefaultOutputDirectory),
            zimdumpExecutablePath = FileTraceLogger.SummarizePath(settings.ZimdumpExecutablePath),
            taskOutputOverride = FileTraceLogger.SummarizePath(settings.TaskOutputOverrideDirectory),
            snapshotIntervalSeconds = settings.SnapshotIntervalSeconds,
            weKnoraBaseUrl = FileTraceLogger.SummarizeText(settings.WeKnoraBaseUrl, 240),
            weKnoraAccessToken = FileTraceLogger.RedactSecret(settings.WeKnoraAccessToken),
            weKnoraKnowledgeBaseId = FileTraceLogger.SummarizeText(settings.WeKnoraKnowledgeBaseId),
            weKnoraKnowledgeBaseName = FileTraceLogger.SummarizeText(settings.WeKnoraKnowledgeBaseName),
            weKnoraKnowledgeBaseDescription = FileTraceLogger.SummarizeText(settings.WeKnoraKnowledgeBaseDescription, 240),
            weKnoraChatModelId = FileTraceLogger.SummarizeText(settings.WeKnoraChatModelId),
            weKnoraEmbeddingModelId = FileTraceLogger.SummarizeText(settings.WeKnoraEmbeddingModelId),
            weKnoraMultimodalModelId = FileTraceLogger.SummarizeText(settings.WeKnoraMultimodalModelId),
            weKnoraChunkSize = settings.WeKnoraChunkSize,
            weKnoraChunkOverlap = settings.WeKnoraChunkOverlap,
            weKnoraEnableParentChild = settings.WeKnoraEnableParentChild,
            weKnoraAuthMode = settings.WeKnoraAuthMode.ToString(),
            weKnoraAutoCreateKnowledgeBase = settings.WeKnoraAutoCreateKnowledgeBase,
            weKnoraAppendMetadataBlock = settings.WeKnoraAppendMetadataBlock,
            historySearchText = FileTraceLogger.SummarizeText(settings.HistorySearchText, 240),
            logSearchText = FileTraceLogger.SummarizeText(settings.LogSearchText, 240),
            settings.SelectedTaskLogsOnly,
            weKnoraSyncSearchText = FileTraceLogger.SummarizeText(settings.WeKnoraSyncSearchText, 240),
            weKnoraSyncLogSearchText = FileTraceLogger.SummarizeText(settings.WeKnoraSyncLogSearchText, 240),
            settings.SelectedWeKnoraSyncLogsOnly,
            settings.MainWindowWidth,
            settings.MainWindowHeight,
            settings.RootSplitterDistance,
            settings.WeKnoraSyncUpperSplitterDistance
        };
    }

    private void ApplyStoredLayoutPreferences(AppSettings settings)
    {
        var windowWidth = Math.Max(MinimumSize.Width, settings.MainWindowWidth > 0 ? settings.MainWindowWidth : DefaultWindowWidth);
        var windowHeight = Math.Max(MinimumSize.Height, settings.MainWindowHeight > 0 ? settings.MainWindowHeight : DefaultWindowHeight);

        if (Width != windowWidth || Height != windowHeight)
        {
            Size = new Size(windowWidth, windowHeight);
        }

        _preferredRootSplitterDistance = settings.RootSplitterDistance > 0
            ? settings.RootSplitterDistance
            : DefaultRootSplitterDistance;

        EnsureRootSplitterDistance();
        ApplyWeKnoraSyncUpperSplitterDistance(settings.WeKnoraSyncUpperSplitterDistance);
    }

    private Size GetPersistedWindowSize()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var width = bounds.Width > 0 ? bounds.Width : Width;
        var height = bounds.Height > 0 ? bounds.Height : Height;
        return new Size(width, height);
    }

    private static object SummarizeToolAvailability(ToolAvailabilityResult availability)
    {
        return new
        {
            availability.IsAvailable,
            resolvedPath = FileTraceLogger.SummarizePath(availability.ResolvedPath),
            version = FileTraceLogger.SummarizeText(availability.Version, 240),
            message = FileTraceLogger.SummarizeText(availability.Message, 320)
        };
    }

    private async Task ScanAndRefreshAsync(bool initialLoad = false)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(ScanAndRefreshAsync), new { initialLoad });

        try
        {
            await SaveSettingsAsync(silent: true);
            SetStatus("Scanning the configured kiwix-desktop directory...");
            await _appService.ScanAsync();
            await RefreshAllViewsAsync(initialLoad);
            SetStatus("ZIM scan completed.");
            scope.Success(new { initialLoad });
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { initialLoad });
            throw;
        }
    }

    private async Task RefreshAllViewsAsync(bool initialLoad = false)
    {
        var shouldTrace = initialLoad || !_refreshTimer.Enabled;
        FileTraceLogger.TraceScope? scope = null;
        if (shouldTrace)
        {
            scope = FileTraceLogger.Enter(nameof(MainForm), nameof(RefreshAllViewsAsync), new { initialLoad, isRefreshing = _isRefreshing, isDisposed = IsDisposed });
        }

        if (_isRefreshing || IsDisposed)
        {
            if (shouldTrace)
            {
                FileTraceLogger.Info(nameof(MainForm), "RefreshAllViewsAsync SKIP", new { initialLoad, isRefreshing = _isRefreshing, isDisposed = IsDisposed });
                scope?.Success(new { skipped = true });
            }

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
            var syncTasks = await _appService.GetWeKnoraSyncTasksAsync(
                string.IsNullOrWhiteSpace(_weKnoraSyncSearchTextBox.Text) ? null : _weKnoraSyncSearchTextBox.Text.Trim());
            var syncLogs = await _appService.GetWeKnoraSyncLogsAsync(
                string.IsNullOrWhiteSpace(_weKnoraSyncLogSearchTextBox.Text) ? null : _weKnoraSyncLogSearchTextBox.Text.Trim(),
                _selectedWeKnoraSyncLogsOnlyCheckBox.Checked ? GetSelectedWeKnoraSyncTaskId() : null,
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

            BindWeKnoraViewData(tasks, syncTasks, syncLogs);

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

            if (shouldTrace)
            {
                scope?.Success(new
                {
                    skipped = false,
                    downloads = downloads.Count,
                    tasks = tasks.Count,
                    history = history.Count,
                    logs = logs.Count,
                    syncTasks = syncTasks.Count,
                    syncLogs = syncLogs.Count
                });
            }
        }
        catch (Exception exception)
        {
            SetStatus("Refresh failed.");
            scope?.Fail(exception, new { initialLoad });
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
            MessageBox.Show(this, T("Select a downloaded ZIM file first."), T("No Selection"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus(T("Starting conversion task..."));
        await SaveSettingsAsync(silent: true);
        await _appService.StartOrResumeTaskAsync(downloadId.Value, string.IsNullOrWhiteSpace(_taskOutputOverrideTextBox.Text) ? null : _taskOutputOverrideTextBox.Text.Trim());
        await RefreshAllViewsAsync();
        SetStatus(T("Conversion task started or resumed."));
    }

    private async Task PauseSelectedTaskAsync()
    {
        var taskId = GetSelectedTaskId();
        if (taskId is null)
        {
            MessageBox.Show(this, T("Select a task row first."), T("No Selection"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus(T("Pausing selected task..."));
        await _appService.PauseTaskAsync(taskId.Value);
        await RefreshAllViewsAsync();
        SetStatus(T("Task paused."));
    }

    private async Task ResumeSelectedTaskAsync()
    {
        var taskId = GetSelectedTaskId();
        if (taskId is null)
        {
            MessageBox.Show(this, T("Select a task row first."), T("No Selection"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus(T("Resuming selected task..."));
        await _appService.ResumeTaskAsync(taskId.Value);
        await RefreshAllViewsAsync();
        SetStatus(T("Task resumed."));
    }

    private async void OnMainFormClosingAsync(object? sender, FormClosingEventArgs e)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(OnMainFormClosingAsync), new
        {
            e.CloseReason,
            allowClose = _allowClose
        });

        if (_allowClose)
        {
            scope.Success(new { skipped = true, reason = "_allowClose already true" });
            return;
        }

        e.Cancel = true;
        _refreshTimer.Stop();
        SetStatus(T("Saving running task state before exit..."));

        try
        {
            await SaveSettingsAsync(silent: true);
            await _appService.PauseAllAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, T("Pause Before Exit Failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _allowClose = true;
        Close();
        scope.Success(new { skipped = false, allowClose = _allowClose });
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

    private static string FormatEta(int processed, int total, DateTime? startedUtc)
    {
        if (!startedUtc.HasValue || processed <= 0 || total <= processed)
        {
            return "n/a";
        }

        var elapsed = DateTime.UtcNow - startedUtc.Value;
        if (elapsed <= TimeSpan.Zero)
        {
            return "n/a";
        }

        var itemsPerSecond = processed / elapsed.TotalSeconds;
        if (itemsPerSecond <= 0)
        {
            return "n/a";
        }

        var remaining = TimeSpan.FromSeconds((total - processed) / itemsPerSecond);
        if (remaining.TotalHours >= 1)
        {
            return remaining.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return remaining.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static void AddLabeledRow(TableLayoutPanel table, int rowIndex, string label, Control control, Control accessory)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, rowIndex);
        table.Controls.Add(control, 1, rowIndex);
        table.Controls.Add(accessory, 2, rowIndex);
    }

    private static void AddStackedInputRow(TableLayoutPanel table, int rowIndex, string label, Control control, Control accessory)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var labelControl = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 0)
        };

        control.Margin = new Padding(3, 3, 6, 3);
        accessory.Anchor = AnchorStyles.Left;

        table.Controls.Add(labelControl, 0, rowIndex);
        table.SetColumnSpan(labelControl, 3);
        table.Controls.Add(control, 0, rowIndex + 1);
        table.SetColumnSpan(control, 2);
        table.Controls.Add(accessory, 2, rowIndex + 1);
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
