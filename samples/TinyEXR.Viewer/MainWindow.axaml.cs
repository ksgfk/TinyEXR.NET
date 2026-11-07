using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TinyEXR.Viewer.Services;
using TinyEXR.Viewer.ViewModels;

namespace TinyEXR.Viewer;

public partial class MainWindow : Window
{
    private readonly string? _initialPath;
    private readonly MainWindowViewModel _viewModel;
    private bool _initialOpenTriggered;

    public MainWindow()
        : this(new MainWindowViewModel(), null)
    {
    }

    public MainWindow(MainWindowViewModel viewModel, string? initialPath)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _initialPath = initialPath;
        DataContext = _viewModel;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, HandleDragOver);
        AddHandler(DragDrop.DropEvent, HandleDrop);

        Opened += HandleOpened;
        Closed += HandleClosed;
    }

    private async void HandleOpened(object? sender, EventArgs e)
    {
        if (_initialOpenTriggered || string.IsNullOrWhiteSpace(_initialPath))
        {
            return;
        }

        _initialOpenTriggered = true;
        await _viewModel.OpenAsync(_initialPath);
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }

    private async void OpenFileClick(object? sender, RoutedEventArgs e)
    {
        IStorageProvider storageProvider = StorageProvider;
        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open EXR Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OpenEXR Image")
                {
                    Patterns = ["*.exr"],
                },
            ],
        });

        string? path = TryGetLocalPath(files.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.OpenAsync(path);
        }
    }

    private void ResetExposureClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Exposure = 0.0;
    }

    private void PreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Grid viewport ||
            e.GetCurrentPoint(viewport).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed ||
            _viewModel.PreviewBitmap is not { } bitmap)
        {
            return;
        }

        Point position = e.GetPosition(viewport);
        if (PreviewPixelMapper.TryGetPixel(viewport.Bounds.Width, viewport.Bounds.Height,
            bitmap.PixelSize.Width, bitmap.PixelSize.Height, position.X, position.Y, out int x, out int y))
        {
            e.Handled = _viewModel.InspectPixel(x, y);
        }
    }

    private void ClearPixelSelectionClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearPixelSelection();
    }

    private void HandleDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetLocalPath(e.DataTransfer.TryGetFiles()?.FirstOrDefault()) is null
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void HandleDrop(object? sender, DragEventArgs e)
    {
        string? path = TryGetLocalPath(e.DataTransfer.TryGetFiles()?.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.OpenAsync(path);
        }

        e.Handled = true;
    }

    private static string? TryGetLocalPath(IStorageItem? item)
    {
        return item?.TryGetLocalPath();
    }
}
