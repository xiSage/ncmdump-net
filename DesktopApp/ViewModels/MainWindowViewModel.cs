using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibNCM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime;
using System.Threading.Tasks;

namespace DesktopApp.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _exportToSource = true;
        [ObservableProperty]
        private bool _keepFolderStructure = false;
        [ObservableProperty]
        private bool _haveFile = false;
        [ObservableProperty]
        private bool _canProcess = true;
        public ObservableCollection<FileItem> FileItems { get; set; } = [];
        public HashSet<string> AddedFiles { get; set; } = [];
        public string? SaveFolder { get; set; } = null;

        public static FilePickerFileType NcmFileType { get; } = new("网易云音乐ncm文件")
        {
            Patterns = ["*.ncm"],
            MimeTypes = null
        };

        public MainWindowViewModel()
        {
        }
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
                string savePath;
                if (ExportToSource || SaveFolder is null)
                {
                    savePath = Path.GetDirectoryName(filePath)!;
                }
                else
                {
                    savePath = SaveFolder;
                }
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
                var files = Directory.GetFiles(folderPath, "*.ncm", SearchOption.AllDirectories);
                foreach (var filePath in files)
                {
                    string savePath;
                    if (ExportToSource || SaveFolder is null)
                    {
                        savePath = Path.GetDirectoryName(filePath)!;
                    }
                    else
                    {
                        savePath = SaveFolder;
                    }
                    AddFile(filePath, savePath);
                }
            }
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
            if (!CanProcess) return;
            CanProcess = false;
            foreach (var fileItem in FileItems)
            {
                await fileItem.Process();
            }
            CanProcess = true;
        }

        private void AddFile(string filePath, string savePath)
        {
            if (AddedFiles.Contains(filePath)) return;
            AddedFiles.Add(filePath);
            var fileItem = new FileItem(filePath, savePath);
            fileItem.RemoveEvent += () =>
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
    }
    public partial class FileItem : ObservableObject
    {
        public delegate void RemoveEventHandler();
        public event RemoveEventHandler? RemoveEvent;

        [ObservableProperty]
        private string _filePath;
        [ObservableProperty]
        private string _savePath;
        public enum StatusEnum { Waiting, Processing, Finished, Failed }
        [ObservableProperty]
        private StatusEnum _status = StatusEnum.Waiting;
        [ObservableProperty]
        private string _message = "等待处理";
        [ObservableProperty]
        private string _statusColor = "Transparent";
        [ObservableProperty]
        private bool _canRemove = true;
        [ObservableProperty]
        private bool _canReset = false;

        public FileItem(string filePath, string savePath)
        {
            FilePath = filePath;
            SavePath = savePath;
        }

        [RelayCommand]
        public void Remove()
        {
            RemoveEvent?.Invoke();
        }

        public async Task Process()
        {
            if (Status != StatusEnum.Waiting) return;
            Status = StatusEnum.Processing;
            Message = "正在处理";
            StatusColor = "Yellow";
            CanRemove = false;
            CanReset = false;
            NeteaseCloudMusicStream? ncm = null;
            try
            {
                ncm = new NeteaseCloudMusicStream(FilePath);
                await ncm.DumpToFileAsync(SavePath, Path.GetFileNameWithoutExtension(FilePath));
                ncm.FixMetadata(true);
                Message = "处理完成";
                StatusColor = "Green";
                CanRemove = true;
                CanReset = false;
                Status = StatusEnum.Finished;
            }
            catch (Exception e)
            {
                Message = e.Message;
                StatusColor = "Red";
                CanRemove = true;
                CanReset = true;
                Status = StatusEnum.Failed;
            }
            finally
            {
                ncm?.Dispose();
            }
        }

        public void Reset()
        {
            Message = "等待处理";
            StatusColor = "Transparent";
            CanRemove = true;
            CanReset = false;
            Status = StatusEnum.Waiting;
        }
    }
}
