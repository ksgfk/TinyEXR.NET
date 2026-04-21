using Avalonia.Media.Imaging;
using TinyEXR.Viewer.Models;
using TinyEXR.Viewer.Services;

namespace TinyEXR.Viewer.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ExrDocumentLoader _documentLoader = new();
    private readonly MetadataFormatter _metadataFormatter = new();

    private CancellationTokenSource? _loadCancellation;
    private ExrViewerDocument? _document;
    private PreviewBuffer? _currentPreviewBuffer;
    private Bitmap? _previewBitmap;
    private PartOption? _selectedPart;
    private LayerOption? _selectedLayer;
    private LevelOption? _selectedLevel;
    private int _previewRevision;
    private bool _suppressSelectionRefresh;
    private bool _isBusy;
    private double _exposure;
    private string _currentPath = "Open an EXR file to begin.";
    private string _previewMessage = "Open an EXR file to begin.";
    private string _emptyPreviewMessage = "Open an EXR file to begin.";
    private IReadOnlyList<PartOption> _partOptions = Array.Empty<PartOption>();
    private IReadOnlyList<LayerOption> _layerOptions = Array.Empty<LayerOption>();
    private IReadOnlyList<LevelOption> _levelOptions = Array.Empty<LevelOption>();
    private IReadOnlyList<KeyValueItem> _overviewEntries = Array.Empty<KeyValueItem>();
    private IReadOnlyList<PartInfoItem> _partEntries = Array.Empty<PartInfoItem>();
    private IReadOnlyList<string> _layerEntries = Array.Empty<string>();
    private IReadOnlyList<ChannelInfoItem> _channelEntries = Array.Empty<ChannelInfoItem>();
    private IReadOnlyList<AttributeInfoItem> _attributeEntries = Array.Empty<AttributeInfoItem>();
    private IReadOnlyList<KeyValueItem> _deepEntries = Array.Empty<KeyValueItem>();

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public string EmptyPreviewMessage
    {
        get => _emptyPreviewMessage;
        private set => SetProperty(ref _emptyPreviewMessage, value);
    }

    public double Exposure
    {
        get => _exposure;
        set
        {
            if (SetProperty(ref _exposure, Math.Round(value, 1)))
            {
                OnPropertyChanged(nameof(ExposureDisplay));
                QueueBitmapRenderFromCurrentBuffer();
            }
        }
    }

    public string ExposureDisplay => $"{Exposure:+0.0;-0.0;0.0} EV";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set
        {
            if (ReferenceEquals(_previewBitmap, value))
            {
                return;
            }

            Bitmap? previous = _previewBitmap;
            _previewBitmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewBitmap));
            OnPropertyChanged(nameof(HasNoPreviewBitmap));
            previous?.Dispose();
        }
    }

    public bool HasPreviewBitmap => PreviewBitmap is not null;

    public bool HasNoPreviewBitmap => PreviewBitmap is null;

    public IReadOnlyList<PartOption> PartOptions
    {
        get => _partOptions;
        private set => SetProperty(ref _partOptions, value);
    }

    public PartOption? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (SetProperty(ref _selectedPart, value) && !_suppressSelectionRefresh)
            {
                HandlePartSelectionChanged();
            }
        }
    }

    public IReadOnlyList<LayerOption> LayerOptions
    {
        get => _layerOptions;
        private set
        {
            if (SetProperty(ref _layerOptions, value))
            {
                OnPropertyChanged(nameof(HasLayerSelection));
            }
        }
    }

    public LayerOption? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (SetProperty(ref _selectedLayer, value) && !_suppressSelectionRefresh)
            {
                HandleRenderableSelectionChanged();
            }
        }
    }

    public IReadOnlyList<LevelOption> LevelOptions
    {
        get => _levelOptions;
        private set
        {
            if (SetProperty(ref _levelOptions, value))
            {
                OnPropertyChanged(nameof(HasLevelSelection));
            }
        }
    }

    public LevelOption? SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value) && !_suppressSelectionRefresh)
            {
                HandleRenderableSelectionChanged();
            }
        }
    }

    public bool HasLayerSelection => LayerOptions.Count > 0;

    public bool HasLevelSelection => LevelOptions.Count > 0;

    public IReadOnlyList<KeyValueItem> OverviewEntries
    {
        get => _overviewEntries;
        private set => SetProperty(ref _overviewEntries, value);
    }

    public IReadOnlyList<PartInfoItem> PartEntries
    {
        get => _partEntries;
        private set => SetProperty(ref _partEntries, value);
    }

    public IReadOnlyList<string> LayerEntries
    {
        get => _layerEntries;
        private set => SetProperty(ref _layerEntries, value);
    }

    public IReadOnlyList<ChannelInfoItem> ChannelEntries
    {
        get => _channelEntries;
        private set => SetProperty(ref _channelEntries, value);
    }

    public IReadOnlyList<AttributeInfoItem> AttributeEntries
    {
        get => _attributeEntries;
        private set => SetProperty(ref _attributeEntries, value);
    }

    public IReadOnlyList<KeyValueItem> DeepEntries
    {
        get => _deepEntries;
        private set
        {
            if (SetProperty(ref _deepEntries, value))
            {
                OnPropertyChanged(nameof(HasDeepEntries));
            }
        }
    }

    public bool HasDeepEntries => DeepEntries.Count > 0;

    public async Task OpenAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _loadCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        IsBusy = true;
        CurrentPath = path;
        PreviewMessage = "Loading EXR document...";
        EmptyPreviewMessage = "Loading EXR document...";

        try
        {
            ExrViewerDocument document = await _documentLoader.LoadAsync(path, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ApplyDocument(document);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ApplyLoadFailure(path, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        PreviewBitmap = null;
    }

    private void ApplyDocument(ExrViewerDocument document)
    {
        _document = document;
        _currentPreviewBuffer = null;
        PreviewBitmap = null;

        PartEntries = _metadataFormatter.BuildPartEntries(document.Parts);
        PartOptions = document.Parts.Select(static part => new PartOption
        {
            PartIndex = part.Index,
            Label = part.DisplayName,
        }).ToArray();

        _suppressSelectionRefresh = true;
        SelectedPart = PartOptions.FirstOrDefault(static option => option.PartIndex == 0) ??
            PartOptions.FirstOrDefault();
        _suppressSelectionRefresh = false;

        HandlePartSelectionChanged();
    }

    private void ApplyLoadFailure(string path, string message)
    {
        _document = null;
        _currentPreviewBuffer = null;
        PartOptions = Array.Empty<PartOption>();
        LayerOptions = Array.Empty<LayerOption>();
        LevelOptions = Array.Empty<LevelOption>();
        PartEntries = Array.Empty<PartInfoItem>();
        LayerEntries = Array.Empty<string>();
        ChannelEntries = Array.Empty<ChannelInfoItem>();
        AttributeEntries = Array.Empty<AttributeInfoItem>();
        DeepEntries = Array.Empty<KeyValueItem>();
        OverviewEntries = Array.Empty<KeyValueItem>();
        PreviewBitmap = null;
        CurrentPath = path;
        PreviewMessage = $"Failed to load file: {message}";
        EmptyPreviewMessage = PreviewMessage;
    }

    private void HandlePartSelectionChanged()
    {
        ExrPartDocument? part = GetSelectedPartDocument();

        LayerEntries = _metadataFormatter.BuildLayerEntries(part);
        DeepEntries = _metadataFormatter.BuildDeepEntries(_document?.DeepDocument);

        _suppressSelectionRefresh = true;
        LayerOptions = BuildLayerOptions(part);
        SelectedLayer = LayerOptions.FirstOrDefault();
        LevelOptions = BuildLevelOptions(part);
        SelectedLevel = LevelOptions.FirstOrDefault();
        _suppressSelectionRefresh = false;

        RefreshMetadata();
        HandleRenderableSelectionChanged();
    }

    private void HandleRenderableSelectionChanged()
    {
        RefreshMetadata();
        ExrPartDocument? part = GetSelectedPartDocument();
        if (part?.Image is null)
        {
            _currentPreviewBuffer = null;
            PreviewBitmap = null;
            PreviewMessage = part is null
                ? "Open an EXR file to begin."
                : "Preview is unavailable for the selected part. Only metadata is shown.";
            EmptyPreviewMessage = PreviewMessage;
            return;
        }

        QueuePreviewComposition(part, SelectedLevel?.LevelIndex ?? 0, SelectedLayer?.LayerName);
    }

    private void RefreshMetadata()
    {
        ExrPartDocument? part = GetSelectedPartDocument();
        OverviewEntries = _metadataFormatter.BuildOverview(_document, part, SelectedLevel, SelectedLayer);
        ChannelEntries = _metadataFormatter.BuildChannelEntries(part, SelectedLevel);
        AttributeEntries = _metadataFormatter.BuildAttributeEntries(part);
        LayerEntries = _metadataFormatter.BuildLayerEntries(part);
    }

    private ExrPartDocument? GetSelectedPartDocument()
    {
        if (_document is null || SelectedPart is null)
        {
            return null;
        }

        return _document.Parts.FirstOrDefault(part => part.Index == SelectedPart.PartIndex);
    }

    private IReadOnlyList<LayerOption> BuildLayerOptions(ExrPartDocument? part)
    {
        if (part is null)
        {
            return Array.Empty<LayerOption>();
        }

        List<LayerOption> layers = new();
        if (part.HasRootLayer)
        {
            layers.Add(new LayerOption { Label = "(root)", LayerName = null });
        }

        for (int i = 0; i < part.NamedLayers.Count; i++)
        {
            string layerName = part.NamedLayers[i];
            layers.Add(new LayerOption { Label = layerName, LayerName = layerName });
        }

        return layers;
    }

    private static IReadOnlyList<LevelOption> BuildLevelOptions(ExrPartDocument? part)
    {
        if (part?.Image is null)
        {
            return Array.Empty<LevelOption>();
        }

        return part.Image.Levels.Select((level, index) => new LevelOption
        {
            LevelIndex = index,
            Label = $"L({level.LevelX}, {level.LevelY}) {level.Width} x {level.Height}",
        }).ToArray();
    }

    private void QueuePreviewComposition(ExrPartDocument part, int levelIndex, string? layerName)
    {
        int revision = Interlocked.Increment(ref _previewRevision);
        PreviewMessage = "Preparing preview...";
        EmptyPreviewMessage = PreviewMessage;

        _ = Task.Run(() => PreviewBitmapRenderer.ComposePreview(part, levelIndex, layerName))
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        ApplyPreviewFailure(revision, $"Failed to prepare preview: {task.Exception?.GetBaseException().Message}");
                        return;
                    }

                    if (task.IsCanceled)
                    {
                        return;
                    }

                    (PreviewBuffer? buffer, string message) result = task.Result;
                    ApplyPreviewBuffer(revision, result.buffer, result.message);
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyPreviewBuffer(int revision, PreviewBuffer? buffer, string message)
    {
        if (revision != _previewRevision)
        {
            return;
        }

        _currentPreviewBuffer = buffer;
        if (buffer is null)
        {
            PreviewBitmap = null;
            PreviewMessage = message;
            EmptyPreviewMessage = message;
            return;
        }

        PreviewMessage = message;
        EmptyPreviewMessage = message;
        QueueBitmapRenderFromCurrentBuffer();
    }

    private void QueueBitmapRenderFromCurrentBuffer()
    {
        PreviewBuffer? buffer = _currentPreviewBuffer;
        if (buffer is null)
        {
            return;
        }

        int revision = Interlocked.Increment(ref _previewRevision);
        PreviewMessage = "Rendering preview...";

        _ = Task.Run(() => PreviewBitmapRenderer.RenderBitmap(buffer, Exposure))
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        ApplyPreviewFailure(revision, $"Failed to render preview: {task.Exception?.GetBaseException().Message}");
                        return;
                    }

                    if (task.IsCanceled || revision != _previewRevision)
                    {
                        task.Result?.Dispose();
                        return;
                    }

                    PreviewBitmap = task.Result;
                    PreviewMessage = _document?.StatusMessage ?? "Preview updated.";
                    EmptyPreviewMessage = "Preview unavailable.";
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyPreviewFailure(int revision, string message)
    {
        if (revision != _previewRevision)
        {
            return;
        }

        _currentPreviewBuffer = null;
        PreviewBitmap = null;
        PreviewMessage = message;
        EmptyPreviewMessage = message;
    }
}
