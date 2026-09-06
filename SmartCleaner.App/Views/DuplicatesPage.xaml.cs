using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartCleaner.App.ViewModels;

namespace SmartCleaner.App.Views;

/// <summary>
/// Code-behind для DuplicatesPage.
/// Обрабатывает Drag-and-Drop и клик по файлу для превью.
/// </summary>
public partial class DuplicatesPage : UserControl
{
    public DuplicatesPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Обработка Drag-Over: разрешаем перетаскивание файлов/папок.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// Обработка Drop: передаём пути файлов/папок во ViewModel.
    /// </summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && DataContext is DuplicatesViewModel vm)
        {
            vm.HandleDrop(paths);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Клик по файлу в списке дубликатов — показывает превью.
    /// </summary>
    private void OnFileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is DuplicateFileViewModel fileVm
            && DataContext is DuplicatesViewModel vm)
        {
            vm.SelectedPreviewFile = fileVm;
        }
    }
}
