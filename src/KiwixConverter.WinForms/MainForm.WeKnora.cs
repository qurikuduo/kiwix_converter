using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.WinForms;

public sealed partial class MainForm
{
    private readonly TextBox _weKnoraBaseUrlTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "http://localhost:8080" };
    private readonly TextBox _weKnoraAccessTokenTextBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _weKnoraKnowledgeBaseNameTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Existing or new knowledge base name" };
    private readonly TextBox _weKnoraKnowledgeBaseIdTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional explicit knowledge base ID" };
    private readonly TextBox _weKnoraKnowledgeBaseDescriptionTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Description used when creating a new knowledge base" };
    private readonly ComboBox _weKnoraChatModelIdComboBox = CreateWeKnoraModelComboBox();
    private readonly ComboBox _weKnoraEmbeddingModelIdComboBox = CreateWeKnoraModelComboBox();
    private readonly ComboBox _weKnoraMultimodalModelIdComboBox = CreateWeKnoraModelComboBox();
    private readonly ComboBox _weKnoraAuthModeComboBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _weKnoraKnowledgeBaseComboBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _weKnoraChunkSizeUpDown = new() { Dock = DockStyle.Fill, Minimum = 100, Maximum = 8192, Increment = 100, Value = 1000 };
    private readonly NumericUpDown _weKnoraChunkOverlapUpDown = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 4096, Increment = 50, Value = 200 };
    private readonly CheckBox _weKnoraAutoCreateKnowledgeBaseCheckBox = new() { Text = "Auto-create the knowledge base when the configured name does not exist", AutoSize = true, Checked = true };
    private readonly CheckBox _weKnoraAppendMetadataCheckBox = new() { Text = "Append export metadata to the synced Markdown content", AutoSize = true, Checked = true };
    private readonly CheckBox _weKnoraEnableParentChildCheckBox = new() { Text = "Enable parent-child chunking for newly created knowledge bases", AutoSize = true };
    private readonly Button _testWeKnoraConnectionButton = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button _loadWeKnoraKnowledgeBasesButton = new() { Text = "Load Knowledge Bases", AutoSize = true };
    private readonly Button _loadWeKnoraModelsButton = new() { Text = "Load Models", AutoSize = true };
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
    private readonly SplitContainer _weKnoraSyncUpperSplitContainer = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        SplitterDistance = 230
    };

    private readonly ProgressBar _weKnoraSyncProgressBar = new() { Dock = DockStyle.Top, Height = 18, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Label _weKnoraSyncSummaryLabel = new() { Dock = DockStyle.Top, AutoSize = true, Text = "No WeKnora sync task selected." };

    private List<WeKnoraKnowledgeBaseInfo> _loadedKnowledgeBases = [];
    private List<WeKnoraModelInfo> _loadedWeKnoraModels = [];

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

    private static ComboBox CreateWeKnoraModelComboBox()
    {
        return new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
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
            RowCount = 14,
            AutoSize = true,
            Padding = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddLabeledRow(table, 0, "Base URL", _weKnoraBaseUrlTextBox, new Label { Text = "The app accepts either the root URL or a full /api/v1 URL.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 1, "Auth Mode", _weKnoraAuthModeComboBox, new Label { Text = "Use API Key for tenant keys or Bearer Token for JWT-style deployments.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 2, "Access Token", _weKnoraAccessTokenTextBox, _testWeKnoraConnectionButton);
        AddLabeledRow(table, 3, "Knowledge Base", _weKnoraKnowledgeBaseComboBox, _loadWeKnoraKnowledgeBasesButton);
        AddLabeledRow(table, 4, "KB Name", _weKnoraKnowledgeBaseNameTextBox, _createWeKnoraKnowledgeBaseButton);
        AddLabeledRow(table, 5, "KB ID", _weKnoraKnowledgeBaseIdTextBox, new Label { Text = "Optional exact target. Leave empty to match by name.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 6, "KB Description", _weKnoraKnowledgeBaseDescriptionTextBox, new Label { Text = "Used when creating or auto-creating a knowledge base.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 7, "Chunk Size", _weKnoraChunkSizeUpDown, new Label { Text = "Chunk size for new knowledge bases.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 8, "Chunk Overlap", _weKnoraChunkOverlapUpDown, new Label { Text = "Overlap applied between adjacent chunks.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 9, "Chat Model ID", _weKnoraChatModelIdComboBox, _loadWeKnoraModelsButton);
        AddLabeledRow(table, 10, "Embedding Model ID", _weKnoraEmbeddingModelIdComboBox, new Label { Text = "Optional Embedding model from /api/v1/models. Leave empty to use the server default.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });
        AddLabeledRow(table, 11, "Multimodal ID", _weKnoraMultimodalModelIdComboBox, new Label { Text = "Optional VLLM model from /api/v1/models.", AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(260, 0) });

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
        optionsPanel.Controls.Add(_weKnoraEnableParentChildCheckBox);
        table.Controls.Add(optionsPanel, 1, 12);
        table.SetColumnSpan(optionsPanel, 2);

        var helperLabel = new Label
        {
            Text = "Choose an existing knowledge base from the server, or type a new KB name and click Create KB. New knowledge bases use the description, chunk size, chunk overlap, and parent-child settings above. Use Load Models to fetch live KnowledgeQA, Embedding, and VLLM model IDs from WeKnora before creating a KB or starting a sync.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(3, 12, 3, 3)
        };
        table.Controls.Add(helperLabel, 1, 13);
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

        _weKnoraSyncUpperSplitContainer.Panel1.Controls.Add(BuildWeKnoraCandidatesGroup());
        _weKnoraSyncUpperSplitContainer.Panel2.Controls.Add(BuildWeKnoraTaskGroup());
        root.Controls.Add(_weKnoraSyncUpperSplitContainer, 0, 2);

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
        _loadWeKnoraModelsButton.Click += async (_, _) => await LoadWeKnoraModelsAsync();
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
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(EnsureZimdumpAvailableAsync), new
        {
            configuredPath = FileTraceLogger.SummarizePath(_zimdumpPathTextBox.Text)
        });

        try
        {
            var availability = await _appService.GetZimdumpAvailabilityAsync();
            if (availability.IsAvailable)
            {
                SetStatus($"zimdump ready: {availability.Version}");
                scope.Success(SummarizeToolAvailability(availability));
                return;
            }

            var result = MessageBox.Show(
                this,
                $"zimdump was not detected.\n\nCurrent check result: {availability.Message}\n\nTo fix this:\n1. Install zimdump from the openZIM zim-tools package.\n2. Add the folder containing zimdump.exe to PATH, or browse directly to zimdump.exe below.\n3. Save settings and retry.\n\nChoose Yes to locate zimdump.exe now, or No to continue with limited functionality.",
                "zimdump Not Found",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            FileTraceLogger.Warning(nameof(MainForm), "EnsureZimdumpAvailableAsync USER_DECISION", new
            {
                availability = SummarizeToolAvailability(availability),
                decision = result.ToString()
            });

            if (result != DialogResult.Yes)
            {
                SetStatus("zimdump not configured. Conversion and metadata extraction will stay unavailable until it is configured.");
                scope.Success(new
                {
                    availability = SummarizeToolAvailability(availability),
                    userChoseToBrowse = false
                });
                return;
            }

            BrowseForExecutable();
            await SaveSettingsAsync(silent: true);

            var retryAvailability = await _appService.GetZimdumpAvailabilityAsync();
            if (retryAvailability.IsAvailable)
            {
                SetStatus($"zimdump ready: {retryAvailability.Version}");
                scope.Success(new
                {
                    availability = SummarizeToolAvailability(retryAvailability),
                    userChoseToBrowse = true
                });
                return;
            }

            MessageBox.Show(this, retryAvailability.Message ?? "zimdump is still unavailable.", "zimdump Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus("zimdump still not configured. Please correct the path or PATH environment variable.");
            scope.Success(new
            {
                availability = SummarizeToolAvailability(retryAvailability),
                userChoseToBrowse = true
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new
            {
                configuredPath = FileTraceLogger.SummarizePath(_zimdumpPathTextBox.Text)
            });
            throw;
        }
    }

    private async Task TryLoadWeKnoraKnowledgeBasesAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(TryLoadWeKnoraKnowledgeBasesAsync), new
        {
            weKnoraBaseUrl = FileTraceLogger.SummarizeText(_weKnoraBaseUrlTextBox.Text, 240),
            weKnoraAccessToken = FileTraceLogger.RedactSecret(_weKnoraAccessTokenTextBox.Text)
        });

        if (string.IsNullOrWhiteSpace(_weKnoraBaseUrlTextBox.Text) || string.IsNullOrWhiteSpace(_weKnoraAccessTokenTextBox.Text))
        {
            scope.Success(new { skipped = true, reason = "Missing base URL or access token" });
            return;
        }

        try
        {
            await LoadWeKnoraKnowledgeBasesAsync(silent: true);
            await LoadWeKnoraModelsAsync(silent: true);
            scope.Success(new { skipped = false });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
        }
    }

    private async Task TestWeKnoraConnectionAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(TestWeKnoraConnectionAsync), CaptureCurrentSettingsInput());

        try
        {
            await SaveSettingsAsync(silent: true);
            var result = await _appService.TestWeKnoraConnectionAsync();
            var icon = result.IsAvailable ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(this, result.Message ?? "No response received.", result.IsAvailable ? "WeKnora Connection OK" : "WeKnora Connection Failed", MessageBoxButtons.OK, icon);

            if (result.IsAvailable)
            {
                await LoadWeKnoraKnowledgeBasesAsync(silent: true);
                await LoadWeKnoraModelsAsync(silent: true);
            }

            scope.Success(new
            {
                result.IsAvailable,
                message = FileTraceLogger.SummarizeText(result.Message, 320)
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private async Task LoadWeKnoraKnowledgeBasesAsync(bool silent = false)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(LoadWeKnoraKnowledgeBasesAsync), new { silent });

        try
        {
            await SaveSettingsAsync(silent: true);
            var knowledgeBases = await _appService.GetWeKnoraKnowledgeBasesAsync();
            PopulateKnowledgeBaseChoices(knowledgeBases);

            if (!silent)
            {
                SetStatus($"Loaded {knowledgeBases.Count} WeKnora knowledge base(s).");
            }

            scope.Success(new
            {
                silent,
                knowledgeBaseCount = knowledgeBases.Count,
                selectedKnowledgeBaseId = FileTraceLogger.SummarizeText(_weKnoraKnowledgeBaseIdTextBox.Text)
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { silent });
            throw;
        }
    }

    private async Task LoadWeKnoraModelsAsync(bool silent = false)
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(LoadWeKnoraModelsAsync), new { silent });

        try
        {
            await SaveSettingsAsync(silent: true);
            var models = await _appService.GetWeKnoraModelsAsync();
            PopulateWeKnoraModelChoices(models);

            if (!silent)
            {
                SetStatus($"Loaded {models.Count} WeKnora model(s).");
            }

            scope.Success(new
            {
                silent,
                modelCount = models.Count,
                selectedChatModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraChatModelIdComboBox)),
                selectedEmbeddingModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraEmbeddingModelIdComboBox)),
                selectedMultimodalModelId = FileTraceLogger.SummarizeText(GetWeKnoraModelSelection(_weKnoraMultimodalModelIdComboBox))
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { silent });
            throw;
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

    private void PopulateWeKnoraModelChoices(IReadOnlyList<WeKnoraModelInfo> models)
    {
        _loadedWeKnoraModels = models.ToList();
        PopulateWeKnoraModelComboBox(_weKnoraChatModelIdComboBox, _loadedWeKnoraModels.Where(static model => string.Equals(model.Type, "KnowledgeQA", StringComparison.OrdinalIgnoreCase)), GetWeKnoraModelSelection(_weKnoraChatModelIdComboBox));
        PopulateWeKnoraModelComboBox(_weKnoraEmbeddingModelIdComboBox, _loadedWeKnoraModels.Where(static model => string.Equals(model.Type, "Embedding", StringComparison.OrdinalIgnoreCase)), GetWeKnoraModelSelection(_weKnoraEmbeddingModelIdComboBox));
        PopulateWeKnoraModelComboBox(_weKnoraMultimodalModelIdComboBox, _loadedWeKnoraModels.Where(static model => string.Equals(model.Type, "VLLM", StringComparison.OrdinalIgnoreCase)), GetWeKnoraModelSelection(_weKnoraMultimodalModelIdComboBox));
    }

    private static void PopulateWeKnoraModelComboBox(ComboBox comboBox, IEnumerable<WeKnoraModelInfo> models, string? selectedModelId)
    {
        var choices = models
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static model => new ModelChoice
            {
                Id = model.Id,
                DisplayText = BuildWeKnoraModelDisplayText(model)
            })
            .ToList();

        choices.Insert(0, new ModelChoice
        {
            Id = string.Empty,
            DisplayText = "(Use server default or leave empty)"
        });

        comboBox.DisplayMember = nameof(ModelChoice.DisplayText);
        comboBox.ValueMember = nameof(ModelChoice.Id);
        comboBox.DataSource = choices;

        if (string.IsNullOrWhiteSpace(selectedModelId))
        {
            comboBox.SelectedIndex = 0;
            return;
        }

        var match = choices.FindIndex(choice => string.Equals(choice.Id, selectedModelId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match >= 0)
        {
            comboBox.SelectedIndex = match;
            return;
        }

        comboBox.SelectedIndex = 0;
        comboBox.Text = selectedModelId.Trim();
    }

    private static string BuildWeKnoraModelDisplayText(WeKnoraModelInfo model)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.Type))
        {
            details.Add(model.Type);
        }

        if (!string.IsNullOrWhiteSpace(model.Provider))
        {
            details.Add(model.Provider);
        }

        if (model.IsDefault)
        {
            details.Add("default");
        }

        return details.Count == 0
            ? $"{model.Name} [{model.Id}]"
            : $"{model.Name} [{model.Id}] - {string.Join(", ", details)}";
    }

    private static string? GetWeKnoraModelSelection(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ModelChoice choice)
        {
            var rawText = comboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawText) || string.Equals(rawText, choice.DisplayText, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(choice.Id) ? null : choice.Id;
            }
        }

        var rawValue = comboBox.Text.Trim();
        return string.IsNullOrWhiteSpace(rawValue) || rawValue.StartsWith("(Use server default", StringComparison.OrdinalIgnoreCase)
            ? null
            : rawValue;
    }

    private async Task CreateWeKnoraKnowledgeBaseAsync()
    {
        using var scope = FileTraceLogger.Enter(nameof(MainForm), nameof(CreateWeKnoraKnowledgeBaseAsync), new
        {
            knowledgeBaseName = FileTraceLogger.SummarizeText(_weKnoraKnowledgeBaseNameTextBox.Text),
            weKnoraBaseUrl = FileTraceLogger.SummarizeText(_weKnoraBaseUrlTextBox.Text, 240)
        });

        var knowledgeBaseName = _weKnoraKnowledgeBaseNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(knowledgeBaseName))
        {
            MessageBox.Show(this, "Enter a knowledge base name before creating it.", "Knowledge Base Name Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            scope.Success(new { created = false, reason = "Knowledge base name missing" });
            return;
        }

        try
        {
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
            scope.Success(new
            {
                created = true,
                knowledgeBaseId = FileTraceLogger.SummarizeText(created.Id),
                knowledgeBaseName = FileTraceLogger.SummarizeText(created.Name)
            });
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { knowledgeBaseName = FileTraceLogger.SummarizeText(knowledgeBaseName) });
            throw;
        }
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

    private sealed class ModelChoice
    {
        public string Id { get; init; } = string.Empty;

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