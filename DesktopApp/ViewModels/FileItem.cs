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

        public enum StatusEnum { Waiting, Processing, Finished, Failed, Cancelled }

        [ObservableProperty]
        public partial StatusEnum Status { get; set; } = StatusEnum.Waiting;

        [ObservableProperty]
        public partial string Message { get; set; } = "等待处理";

        [ObservableProperty]
        public partial IBrush StatusColor { get; set; } = StatusWaitingBrush;

        [ObservableProperty]
        public partial bool CanRemove { get; set; } = true;

        [ObservableProperty]
        public partial bool CanReset { get; set; } = false;

        /// <summary>状态徽章文字，随 <see cref="Status"/> 联动。</summary>
        public string StatusText => Status switch
        {
            StatusEnum.Waiting => "等待处理",
            StatusEnum.Processing => "处理中",
            StatusEnum.Finished => "完成",
            StatusEnum.Failed => "失败",
            StatusEnum.Cancelled => "已取消",
            _ => Status.ToString()
        };

        // 浅/深色下都可读的中性色调，替代默认色板。
        private static readonly IBrush StatusWaitingBrush = new SolidColorBrush(0xFF929292);
        private static readonly IBrush StatusProcessingBrush = new SolidColorBrush(0xFFF5A623);
        private static readonly IBrush StatusFinishedBrush = new SolidColorBrush(0xFF34C759);
        private static readonly IBrush StatusFailedBrush = new SolidColorBrush(0xFFE5484D);

        partial void OnStatusChanged(StatusEnum value)
        {
            OnPropertyChanged(nameof(StatusText));
        }

        [RelayCommand]
        public void Remove()
        {
            RemoveCallback?.Invoke();
        }

        [RelayCommand]
        public void Reset()
        {
            Message = "等待处理";
            StatusColor = StatusWaitingBrush;
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
            StatusColor = StatusProcessingBrush;
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
                    StatusColor = StatusFailedBrush;
                    CanRemove = true;
                    CanReset = true;
                    Status = StatusEnum.Failed;
                    return;
                }

                Message = result.MetadataWarning is null ? "处理完成" : "处理完成（元数据写入失败）";
                StatusColor = StatusFinishedBrush;
                CanRemove = true;
                CanReset = false;
                Status = StatusEnum.Finished;
            }
            catch (OperationCanceledException)
            {
                Message = "已取消";
                StatusColor = StatusWaitingBrush;
                CanRemove = true;
                CanReset = true;
                Status = StatusEnum.Cancelled;
                throw;
            }
            catch (Exception e)
            {
                Message = e.Message;
                StatusColor = StatusFailedBrush;
                CanRemove = true;
                CanReset = true;
                Status = StatusEnum.Failed;
            }
        }
    }
}