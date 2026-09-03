using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopApp.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        public partial bool ExportToSource { get; set; } = true;

        [ObservableProperty]
        public partial bool HaveFile { get; set; } = false;

        [ObservableProperty]
        public partial bool CanProcess { get; set; } = true;

        [ObservableProperty]
        public partial string? SaveFolder { get; set; } = null;

        public ObservableCollection<FileItem> FileItems { get; } = [];
        public HashSet<string> AddedFiles { get; } = [];

        /// <summary>任务列表为空（用于空状态引导的可见性）。</summary>
        public bool IsEmpty => !HaveFile;

        private CancellationTokenSource? _processCts;

        public bool CanStartProcessing => CanProcess && HaveFile;

        public static FilePickerFileType NcmFileType { get; } = new("网易云音乐ncm文件")
        {
            Patterns = ["*.ncm"],
            MimeTypes = null
        };

        [RelayCommand]
        public async Task SelectFile()
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
            if (window == null) return;
            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择一个或多个ncm文件",
                AllowMultiple = true,
                FileTypeFilter = [NcmFileType]
            });
            foreach (var file in files)
            {
                var filePath = file.Path.LocalPath;
                string savePath = ExportToSource || SaveFolder is null ? Path.GetDirectoryName(filePath)! : SaveFolder;
                AddFile(filePath, savePath);
            }
        }

        [RelayCommand]
        public async Task SelectFolder()
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
            if (window == null) return;
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择一个或多个文件夹",
                AllowMultiple = true
            });
            foreach (var folder in folders)
            {
                var folderPath = folder.Path.LocalPath;
                string[] files;
                try
                {
                    files = Directory.GetFiles(folderPath, "*.ncm", SearchOption.AllDirectories);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Failed to scan folder \"{folderPath}\": {e.Message}");
                    continue;
                }
                AddFiles(files);
            }
        }

        [RelayCommand]
        public void DropFiles(IEnumerable<IStorageItem> files)
        {
            AddFiles(
                files
                .Select(file => file.Path.LocalPath)
                .Where(file => file.EndsWith(".ncm", StringComparison.OrdinalIgnoreCase))
            );
        }

        [RelayCommand]
        public async Task GetSaveFolder()
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
            if (window == null) return;
            var folder = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择保存文件夹",
                AllowMultiple = false
            });
            if (folder is not null && folder.Count > 0)
            {
                SaveFolder = folder[0].Path.LocalPath;
            }
            UpdateSavePath();
        }

        [RelayCommand]
        public void ClearFiles()
        {
            FileItems.Clear();
            AddedFiles.Clear();
            HaveFile = false;
        }

        [RelayCommand]
        public void ClearFinishedFiles()
        {
            for (int i = 0; i < FileItems.Count;)
            {
                FileItem? fileItem = FileItems[i];
                if (fileItem.Status == FileItem.StatusEnum.Finished)
                {
                    fileItem.Remove();
                    continue;
                }
                i++;
            }
            if (FileItems.Count == 0)
            {
                HaveFile = false;
            }
        }

        [RelayCommand]
        public async Task ProcessFiles()
        {
            if (!CanProcess || _processCts is not null) return;
            CanProcess = false;
            _processCts = new CancellationTokenSource();
            var token = _processCts.Token;
            using var semaphore = new SemaphoreSlim(4);
            try
            {
                await Task.WhenAll(FileItems.Select(async item =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        await item.Process(token);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            finally
            {
                _processCts.Dispose();
                _processCts = null;
                CanProcess = true;
            }
        }

        [RelayCommand]
        public void StopProcessing()
        {
            _processCts?.Cancel();
        }

        private void AddFile(string filePath, string savePath)
        {
            if (AddedFiles.Contains(filePath)) return;
            AddedFiles.Add(filePath);
            var fileItem = new FileItem(filePath, savePath);
            fileItem.RemoveCallback = () =>
            {
                AddedFiles.Remove(filePath);
                FileItems.Remove(fileItem);
                if (FileItems.Count == 0)
                {
                    HaveFile = false;
                }
            };
            FileItems.Add(fileItem);
            HaveFile = true;
        }

        private void AddFiles(IEnumerable<string> files)
        {
            foreach (var filePath in files)
            {
                string savePath = ExportToSource || SaveFolder is null ? Path.GetDirectoryName(filePath)! : SaveFolder;
                AddFile(filePath, savePath);
            }
        }

        private void UpdateSavePath()
        {
            if (ExportToSource || SaveFolder is null)
            {
                foreach (var fileItem in FileItems)
                {
                    fileItem.SavePath = Path.GetDirectoryName(fileItem.FilePath)!;
                }
            }
            else
            {
                foreach (var fileItem in FileItems)
                {
                    fileItem.SavePath = SaveFolder;
                }
            }
        }

        partial void OnExportToSourceChanged(bool oldValue, bool newValue)
        {
            UpdateSavePath();
        }

        partial void OnCanProcessChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(CanStartProcessing));
        }

        partial void OnHaveFileChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(CanStartProcessing));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
