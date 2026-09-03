using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibNCM;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopApp.ViewModels
{
    public partial class FileItem(string filePath, string savePath) : ObservableObject
    {
        public Action? RemoveCallback { get; set; }

        [ObservableProperty]
        public partial string FilePath { get; set; } = filePath;

        [ObservableProperty]
        public partial string SavePath { get; set; } = savePath;

        public enum StatusEnum { Waiting, Processing, Finished, Failed }

        [ObservableProperty]
        public partial StatusEnum Status { get; set; } = StatusEnum.Waiting;

        [ObservableProperty]
        public partial string Message { get; set; } = "等待处理";

        [ObservableProperty]
        public partial IBrush StatusColor { get; set; } = Brushes.Transparent;

        [ObservableProperty]
        public partial bool CanRemove { get; set; } = true;

        [ObservableProperty]
        public partial bool CanReset { get; set; } = false;

        [RelayCommand]
        public void Remove()
        {
            RemoveCallback?.Invoke();
        }

        [RelayCommand]
        public void Reset()
        {
            Message = "等待处理";
            StatusColor = Brushes.Transparent;
            CanRemove = true;
            CanReset = false;
            Status = StatusEnum.Waiting;
        }

        public async Task Process(CancellationToken cancellationToken = default)
        {
            if (Status != StatusEnum.Waiting) return;
            cancellationToken.ThrowIfCancellationRequested();
            Status = StatusEnum.Processing;
            Message = "正在处理";
            StatusColor = Brushes.Yellow;
            CanRemove = false;
            CanReset = false;
            try
            {
                var result = await NcmProcessor.ProcessAsync(
                    FilePath, SavePath, Path.GetFileNameWithoutExtension(FilePath),
                    options: null, coverArtProvider: null, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (!result.Success)
                {
                    Message = result.ErrorMessage ?? "未知错误";
                    StatusColor = Brushes.Red;
                    CanRemove = true;
                    CanReset = true;
                    Status = StatusEnum.Failed;
                    return;
                }

                Message = result.MetadataWarning is null ? "处理完成" : "处理完成（元数据写入失败）";
                StatusColor = Brushes.Green;
                CanRemove = true;
                CanReset = false;
                Status = StatusEnum.Finished;
            }
            catch (OperationCanceledException)
            {
                Message = "已取消";
                StatusColor = Brushes.Gray;
                CanRemove = true;
                CanReset = true;
                Status = StatusEnum.Waiting;
                throw;
            }
            catch (Exception e)
            {
                Message = e.Message;
                StatusColor = Brushes.Red;
                CanRemove = true;
                CanReset = true;
                Status = StatusEnum.Failed;
            }
        }
    }
}
