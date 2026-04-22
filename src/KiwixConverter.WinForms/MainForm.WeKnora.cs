using KiwixConverter.Core.Models;

namespace KiwixConverter.WinForms;

public sealed partial class MainForm
{
    private readonly TextBox _weKnoraBaseUrlTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "http://localhost:8080" };
    private readonly TextBox _weKnoraAccessTokenTextBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _weKnoraKnowledgeBaseNameTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Existing or new knowledge base name" };
    private readonly TextBox _weKnoraKnowledgeBaseIdTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional explicit knowledge base ID" };
    private readonly TextBox _weKnoraChatModelIdTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional KnowledgeQA model ID" };
    private readonly TextBox _weKnoraMultimodalModelIdTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional VLLM model ID" };
    private readonly ComboBox _weKnoraAuthModeComboBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _weKnoraKnowledgeBaseComboBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _weKnoraAutoCreateKnowledgeBaseCheckBox = new() { Text = "Auto-create the knowledge base when the configured name does not exist", AutoSize = true, Checked = true };
    private readonly CheckBox _weKnoraAppendMetadataCheckBox = new() { Text = "Append export metadata to the synced Markdown content", AutoSize = true, Checked = true };
    private readonly Button _testWeKnoraConnectionButton = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button _loadWeKnoraKnowledgeBasesButton = new() { Text = "Load Knowledge Bases", AutoSize = true };
    private readonly Button _createWeKnoraKnowledgeBaseButton = new() { Text = "Create KB", AutoSize = true };
    private readonly Button _startWeKnoraSyncButton = new() { Text = "Sync Selected Archives", AutoSize = true };
    private readonly Button _pauseWeKnoraSyncButton = new() { Text = "Pause Selected Sync", AutoSize = true };
    private readonly Button _resumeWeKnoraSyncButton = new() { Text = "Resume Selected Sync", AutoSize = true };
    private readonly Button _refreshWeKnoraSyncButton = new() { Text = "Refresh Sync View", AutoSize = true };
    private readonly TextBox _weKnoraSyncSearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Search sync tasks by archive, knowledge base, status or error..." };
    private readonly TextBox _weKnoraSyncLogSearchTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Search WeKnora sync logs by message or article URL..." };
    private readonly CheckBox _selectedWeKnoraSyncLogsOnlyCheckBox = new() { Text = "Selected sync only", AutoSize = true };

    private readonly DataGridView _weKnoraSyncCandidatesGrid = CreateGrid();
    private readonly DataGridView _weKnoraSyncTasksGrid = CreateGrid();
    private readonly DataGridView _weKnoraSyncLogsGrid = CreateGrid();

    private readonly ProgressBar _weKnoraSyncProgressBar = new() { Dock = DockStyle.Top, Height = 18, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Label _weKnoraSyncSummaryLabel = new() { Dock = DockStyle.Top, AutoSize = true, Text = "No WeKnora sync task selected." };

    private List<WeKnoraKnowledgeBaseInfo> _loadedKnowledgeBases = [];

    private void InitializeWeKnoraControls()
    {
        _weKnoraAuthModeComboBox.Items.AddRange([WeKnoraAuthMode.ApiKey.ToString(), WeKnoraAuthMode.BearerToken.ToString()]);
        _weKnoraAuthModeComboBox.SelectedIndex = 0;

        _weKnoraKnowledgeBaseComboBox.DisplayMember = nameof(KnowledgeBaseChoice.DisplayText);
        _weKnoraKnowledgeBaseComboBox.ValueMember = nameof(KnowledgeBaseChoice.Id);

        _weKnoraSyncCandidatesGrid.MultiSelect = true;
        _weKnoraSyncTasksGrid.MultiSelect = false;
        _weKnoraSyncLogsGrid.MultiSelect = false;
    }

    private Control BuildWeKnoraSettingsGroup()
    {
        var group = new GroupBox
        {
            Text = "WeKnora Sync Configuration",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 10,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddLabeledRow(table, 0, "Base URL", _weKnoraBaseUrlTextBox, new Label { Text = "The app accepts either the root URL or a full /api/v1 URL.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 1, "Auth Mode", _weKnoraAuthModeComboBox, new Label { Text = "Use API Key for tenant keys or Bearer Token for JWT-style deployments.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 2, "Access Token", _weKnoraAccessTokenTextBox, _testWeKnoraConnectionButton);
        AddLabeledRow(table, 3, "Knowledge Base", _weKnoraKnowledgeBaseComboBox, _loadWeKnoraKnowledgeBasesButton);
        AddLabeledRow(table, 4, "KB Name", _weKnoraKnowledgeBaseNameTextBox, _createWeKnoraKnowledgeBaseButton);
        AddLabeledRow(table, 5, "KB ID", _weKnoraKnowledgeBaseIdTextBox, new Label { Text = "Optional exact target. Leave empty to match by name.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 6, "Chat Model ID", _weKnoraChatModelIdTextBox, new Label { Text = "Optional KnowledgeQA model from /api/v1/models.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 7, "Multimodal ID", _weKnoraMultimodalModelIdTextBox, new Label { Text = "Optional VLLM model from /api/v1/models.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });

        var optionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(3, 6, 3, 3)
        };
        optionsPanel.Controls.Add(_weKnoraAutoCreateKnowledgeBaseCheckBox);
        optionsPanel.Controls.Add(_weKnoraAppendMetadataCheckBox);
        table.Controls.Add(optionsPanel, 1, 8);
        table.SetColumnSpan(optionsPanel, 2);

        var helperLabel = new Label
        {
            Text = "Choose an existing knowledge base from the server, or type a new KB name and click Create KB. Optional chat and multimodal model IDs are applied when a KB is created and again before each sync. The current sync implementation uploads each exported article as manual Markdown knowledge and keeps per-article checkpoints for pause/resume recovery.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(3, 12, 3, 3)
        };
        table.Controls.Add(helperLabel, 1, 9);
        table.SetColumnSpan(helperLabel, 2);

        group.Controls.Add(table);
        return group;
    }

    private TabPage BuildWeKnoraSyncTab()
    {
        var tab = new TabPage("WeKnora Sync");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "Select one or more completed conversion outputs, start a sync, then watch progress, ETA, logs, and resume state from the same screen.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(880, 0)
        }, 0, 0);

        var controlsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 6,
            RowCount = 1,
            AutoSize = true
        };
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsPanel.Controls.Add(_weKnoraSyncSearchTextBox, 0, 0);
        controlsPanel.Controls.Add(_startWeKnoraSyncButton, 1, 0);
        controlsPanel.Controls.Add(_pauseWeKnoraSyncButton, 2, 0);
        controlsPanel.Controls.Add(_resumeWeKnoraSyncButton, 3, 0);
        controlsPanel.Controls.Add(_refreshWeKnoraSyncButton, 4, 0);
        controlsPanel.Controls.Add(new Label { Text = "Select rows in the candidate grid to create sync tasks.", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) }, 5, 0);
        root.Controls.Add(controlsPanel, 0, 1);

        var upperSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 230
        };
        upperSplit.Panel1.Controls.Add(BuildWeKnoraCandidatesGroup());
        upperSplit.Panel2.Controls.Add(BuildWeKnoraTaskGroup());
        root.Controls.Add(upperSplit, 0, 2);

        root.Controls.Add(BuildWeKnoraLogsGroup(), 0, 3);

        tab.Controls.Add(root);
        return tab;
    }

    private Control BuildWeKnoraCandidatesGroup()
    {
        var group = new GroupBox
        {
            Text = "Converted Archives Ready To Sync",
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
            Text = "Each row represents a completed conversion task. The sync task will upload every completed article export from the selected archive to the configured WeKnora knowledge base.",
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(860, 0)
        }, 0, 0);
        layout.Controls.Add(_weKnoraSyncCandidatesGrid, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildWeKnoraTaskGroup()
    {
        var group = new GroupBox
        {
            Text = "WeKnora Sync Tasks",
            Dock = DockStyle.Fill
        };

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

        layout.Controls.Add(_weKnoraSyncSummaryLabel, 0, 0);
        layout.Controls.Add(_weKnoraSyncProgressBar, 0, 1);
        layout.Controls.Add(_weKnoraSyncTasksGrid, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildWeKnoraLogsGroup()
    {
        var group = new GroupBox
        {
            Text = "WeKnora Sync Logs",
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
        filterPanel.Controls.Add(_weKnoraSyncLogSearchTextBox, 1, 0);
        filterPanel.Controls.Add(_selectedWeKnoraSyncLogsOnlyCheckBox, 2, 0);
        filterPanel.Controls.Add(new Label { Text = "Select a sync task row to scope logs.", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) }, 3, 0);
        filterPanel.Controls.Add(new Label { Text = "Use Refresh Sync View after changing filters.", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) }, 4, 0);

        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(_weKnoraSyncLogsGrid, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private void WireWeKnoraEvents()
    {
        _testWeKnoraConnectionButton.Click += async (_, _) => await TestWeKnoraConnectionAsync();
        _loadWeKnoraKnowledgeBasesButton.Click += async (_, _) => await LoadWeKnoraKnowledgeBasesAsync();
        _createWeKnoraKnowledgeBaseButton.Click += async (_, _) => await CreateWeKnoraKnowledgeBaseAsync();
        _startWeKnoraSyncButton.Click += async (_, _) => await StartSelectedWeKnoraSyncAsync();
        _pauseWeKnoraSyncButton.Click += async (_, _) => await PauseSelectedWeKnoraSyncAsync();
        _resumeWeKnoraSyncButton.Click += async (_, _) => await ResumeSelectedWeKnoraSyncAsync();
        _refreshWeKnoraSyncButton.Click += async (_, _) => await RefreshAllViewsAsync();
        _weKnoraKnowledgeBaseComboBox.SelectionChangeCommitted += (_, _) => ApplySelectedKnowledgeBaseChoice();
        _weKnoraSyncTasksGrid.SelectionChanged += (_, _) => UpdateWeKnoraSyncSummary();
    }

    private async Task EnsureZimdumpAvailableAsync()
    {
        var availability = await _appService.GetZimdumpAvailabilityAsync();
        if (availability.IsAvailable)
        {
            SetStatus($"zimdump ready: {availability.Version}");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"zimdump was not detected.\n\nCurrent check result: {availability.Message}\n\nTo fix this:\n1. Install zimdump from the openZIM zim-tools package.\n2. Add the folder containing zimdump.exe to PATH, or browse directly to zimdump.exe below.\n3. Save settings and retry.\n\nChoose Yes to locate zimdump.exe now, or No to continue with limited functionality.",
            "zimdump Not Found",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            SetStatus("zimdump not configured. Conversion and metadata extraction will stay unavailable until it is configured.");
            return;
        }

        BrowseForExecutable();
        await SaveSettingsAsync(silent: true);

        var retryAvailability = await _appService.GetZimdumpAvailabilityAsync();
        if (retryAvailability.IsAvailable)
        {
            SetStatus($"zimdump ready: {retryAvailability.Version}");
            return;
        }

        MessageBox.Show(this, retryAvailability.Message ?? "zimdump is still unavailable.", "zimdump Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SetStatus("zimdump still not configured. Please correct the path or PATH environment variable.");
    }

    private async Task TryLoadWeKnoraKnowledgeBasesAsync()
    {
        if (string.IsNullOrWhiteSpace(_weKnoraBaseUrlTextBox.Text) || string.IsNullOrWhiteSpace(_weKnoraAccessTokenTextBox.Text))
        {
            return;
        }

        try
        {
            await LoadWeKnoraKnowledgeBasesAsync(silent: true);
        }
        catch
        {
        }
    }

    private async Task TestWeKnoraConnectionAsync()
    {
        await SaveSettingsAsync(silent: true);
        var result = await _appService.TestWeKnoraConnectionAsync();
        var icon = result.IsAvailable ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
        MessageBox.Show(this, result.Message ?? "No response received.", result.IsAvailable ? "WeKnora Connection OK" : "WeKnora Connection Failed", MessageBoxButtons.OK, icon);

        if (result.IsAvailable)
        {
            await LoadWeKnoraKnowledgeBasesAsync(silent: true);
        }
    }

    private async Task LoadWeKnoraKnowledgeBasesAsync(bool silent = false)
    {
        await SaveSettingsAsync(silent: true);
        var knowledgeBases = await _appService.GetWeKnoraKnowledgeBasesAsync();
        PopulateKnowledgeBaseChoices(knowledgeBases);

        if (!silent)
        {
            SetStatus($"Loaded {knowledgeBases.Count} WeKnora knowledge base(s).");
        }
    }

    private void PopulateKnowledgeBaseChoices(IReadOnlyList<WeKnoraKnowledgeBaseInfo> knowledgeBases)
    {
        _loadedKnowledgeBases = knowledgeBases.ToList();
        var choices = knowledgeBases
            .Select(static kb => new KnowledgeBaseChoice
            {
                Id = kb.Id,
                Name = kb.Name,
                DisplayText = string.IsNullOrWhiteSpace(kb.Description)
                    ? $"{kb.Name} [{kb.Id}]"
                    : $"{kb.Name} [{kb.Id}] - {kb.Description}"
            })
            .ToList();

        _weKnoraKnowledgeBaseComboBox.DataSource = choices;
        if (choices.Count == 0)
        {
            return;
        }

        var match = choices.FindIndex(choice => string.Equals(choice.Id, _weKnoraKnowledgeBaseIdTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        _weKnoraKnowledgeBaseComboBox.SelectedIndex = match >= 0 ? match : 0;
        ApplySelectedKnowledgeBaseChoice();
    }

    private void ApplySelectedKnowledgeBaseChoice()
    {
        if (_weKnoraKnowledgeBaseComboBox.SelectedItem is not KnowledgeBaseChoice choice)
        {
            return;
        }

        _weKnoraKnowledgeBaseIdTextBox.Text = choice.Id;
        _weKnoraKnowledgeBaseNameTextBox.Text = choice.Name;
    }

    private async Task CreateWeKnoraKnowledgeBaseAsync()
    {
        var knowledgeBaseName = _weKnoraKnowledgeBaseNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(knowledgeBaseName))
        {
            MessageBox.Show(this, "Enter a knowledge base name before creating it.", "Knowledge Base Name Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await SaveSettingsAsync(silent: true);
        var created = await _appService.CreateWeKnoraKnowledgeBaseAsync(knowledgeBaseName);
        _weKnoraKnowledgeBaseIdTextBox.Text = created.Id;
        _weKnoraKnowledgeBaseNameTextBox.Text = created.Name;
        await LoadWeKnoraKnowledgeBasesAsync(silent: true);

        var selectedIndex = (_weKnoraKnowledgeBaseComboBox.DataSource as List<KnowledgeBaseChoice>)
            ?.FindIndex(choice => string.Equals(choice.Id, created.Id, StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (selectedIndex >= 0)
        {
            _weKnoraKnowledgeBaseComboBox.SelectedIndex = selectedIndex;
        }

        SetStatus($"Created WeKnora knowledge base '{created.Name}'.");
    }

    private async Task StartSelectedWeKnoraSyncAsync()
    {
        var sourceTaskIds = GetSelectedWeKnoraSourceTaskIds();
        if (sourceTaskIds.Count == 0)
        {
            MessageBox.Show(this, "Select one or more completed conversion rows in the 'Converted Archives Ready To Sync' grid first.", "No Sync Source Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await SaveSettingsAsync(silent: true);
        foreach (var sourceTaskId in sourceTaskIds)
        {
            await _appService.StartOrResumeWeKnoraSyncAsync(sourceTaskId);
        }

        await RefreshAllViewsAsync();
        SetStatus($"Started or resumed {sourceTaskIds.Count} WeKnora sync task(s).");
    }

    private async Task PauseSelectedWeKnoraSyncAsync()
    {
        var syncTaskId = GetSelectedWeKnoraSyncTaskId();
        if (syncTaskId is null)
        {
            MessageBox.Show(this, "Select a WeKnora sync task row first.", "No Sync Task Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _appService.PauseWeKnoraSyncTaskAsync(syncTaskId.Value);
        await RefreshAllViewsAsync();
        SetStatus("WeKnora sync task paused.");
    }

    private async Task ResumeSelectedWeKnoraSyncAsync()
    {
        var syncTaskId = GetSelectedWeKnoraSyncTaskId();
        if (syncTaskId is null)
        {
            MessageBox.Show(this, "Select a WeKnora sync task row first.", "No Sync Task Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _appService.ResumeWeKnoraSyncTaskAsync(syncTaskId.Value);
        await RefreshAllViewsAsync();
        SetStatus("WeKnora sync task resumed.");
    }

    private void BindWeKnoraViewData(
        IReadOnlyList<ConversionTaskRecord> conversionTasks,
        IReadOnlyList<WeKnoraSyncTaskRecord> syncTasks,
        IReadOnlyList<WeKnoraSyncLogEntryRecord> syncLogs)
    {
        _weKnoraSyncCandidatesGrid.DataSource = conversionTasks
            .Where(static task => task.Status == ConversionTaskStatus.Completed)
            .Select(static task => new WeKnoraSyncCandidateRow
            {
                SourceTaskId = task.Id,
                Archive = Path.GetFileName(task.ZimPath),
                Progress = task.TotalArticles > 0 ? $"{task.ProcessedArticles}/{task.TotalArticles}" : task.ProcessedArticles.ToString(),
                CompletedLocal = task.CompletedUtc?.ToLocalTime(),
                OutputDirectory = task.OutputDirectory
            })
            .ToList();

        _weKnoraSyncTasksGrid.DataSource = syncTasks.Select(static task => new WeKnoraSyncTaskRow
        {
            Id = task.Id,
            SourceTaskId = task.SourceTaskId,
            Archive = task.SourceArchiveKey,
            KnowledgeBase = string.IsNullOrWhiteSpace(task.KnowledgeBaseName) ? task.KnowledgeBaseId : $"{task.KnowledgeBaseName} [{task.KnowledgeBaseId}]",
            Status = task.Status.ToString(),
            Progress = task.TotalDocuments > 0 ? $"{task.ProcessedDocuments}/{task.TotalDocuments}" : task.ProcessedDocuments.ToString(),
            ProgressPercent = task.TotalDocuments > 0 ? Math.Max(0, Math.Min(100, (int)Math.Round(task.ProcessedDocuments * 100d / task.TotalDocuments))) : 0,
            Failed = task.FailedDocuments,
            Eta = FormatEta(task.ProcessedDocuments, task.TotalDocuments, task.StartedUtc),
            CurrentArticle = task.CurrentArticleUrl ?? string.Empty,
            StartedLocal = task.StartedUtc?.ToLocalTime(),
            CompletedLocal = task.CompletedUtc?.ToLocalTime(),
            Error = task.ErrorMessage ?? string.Empty
        }).ToList();

        _weKnoraSyncLogsGrid.DataSource = syncLogs.Select(static log => new WeKnoraSyncLogRow
        {
            Id = log.Id,
            SyncTaskId = log.SyncTaskId,
            TimeLocal = log.TimestampUtc.ToLocalTime(),
            Level = log.Level.ToString(),
            Category = log.Category,
            Message = log.Message,
            ArticleUrl = log.ArticleUrl ?? string.Empty,
            Details = log.Details ?? string.Empty
        }).ToList();

        UpdateWeKnoraSyncSummary();
    }

    private void UpdateWeKnoraSyncSummary()
    {
        var row = _weKnoraSyncTasksGrid.CurrentRow?.DataBoundItem as WeKnoraSyncTaskRow
            ?? (_weKnoraSyncTasksGrid.DataSource as List<WeKnoraSyncTaskRow>)?.FirstOrDefault(static task => task.Status == nameof(ConversionTaskStatus.Running));

        if (row is null)
        {
            _weKnoraSyncProgressBar.Value = 0;
            _weKnoraSyncSummaryLabel.Text = "No WeKnora sync task selected.";
            return;
        }

        _weKnoraSyncProgressBar.Value = Math.Max(_weKnoraSyncProgressBar.Minimum, Math.Min(_weKnoraSyncProgressBar.Maximum, row.ProgressPercent));
        _weKnoraSyncSummaryLabel.Text = $"Archive: {row.Archive} | Target: {row.KnowledgeBase} | Status: {row.Status} | Progress: {row.Progress} | Failed: {row.Failed} | ETA: {row.Eta}";
    }

    private List<long> GetSelectedWeKnoraSourceTaskIds()
    {
        return _weKnoraSyncCandidatesGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(static row => row.DataBoundItem)
            .OfType<WeKnoraSyncCandidateRow>()
            .Select(static row => row.SourceTaskId)
            .Distinct()
            .ToList();
    }

    private long? GetSelectedWeKnoraSyncTaskId()
    {
        return _weKnoraSyncTasksGrid.CurrentRow?.DataBoundItem is WeKnoraSyncTaskRow row ? row.Id : null;
    }

    private sealed class KnowledgeBaseChoice
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string DisplayText { get; init; } = string.Empty;
    }

    private sealed class WeKnoraSyncCandidateRow
    {
        public long SourceTaskId { get; init; }

        public string Archive { get; init; } = string.Empty;

        public string Progress { get; init; } = string.Empty;

        public DateTime? CompletedLocal { get; init; }

        public string OutputDirectory { get; init; } = string.Empty;
    }

    private sealed class WeKnoraSyncTaskRow
    {
        public long Id { get; init; }

        public long SourceTaskId { get; init; }

        public string Archive { get; init; } = string.Empty;

        public string KnowledgeBase { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Progress { get; init; } = string.Empty;

        public int ProgressPercent { get; init; }

        public int Failed { get; init; }

        public string Eta { get; init; } = string.Empty;

        public string CurrentArticle { get; init; } = string.Empty;

        public DateTime? StartedLocal { get; init; }

        public DateTime? CompletedLocal { get; init; }

        public string Error { get; init; } = string.Empty;
    }

    private sealed class WeKnoraSyncLogRow
    {
        public long Id { get; init; }

        public long? SyncTaskId { get; init; }

        public DateTime TimeLocal { get; init; }

        public string Level { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string ArticleUrl { get; init; } = string.Empty;

        public string Details { get; init; } = string.Empty;
    }
}