using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using System;
using System.Threading.Tasks;

namespace DesktopApp.Views
{
    public partial class MainWindowView : Window
    {
        private const string GitHubUrl = "https://github.com/xiSage/ncmdump-net";

        public MainWindowView()
        {
            InitializeComponent();
        }

        /// <summary>用系统默认浏览器打开项目主页；打不开时把地址摆出来，可复制。</summary>
        private async void OnOpenGitHubClick(object? sender, RoutedEventArgs e)
        {
            if (await TryLaunchAsync()) return;

            var dialog = new FAContentDialog
            {
                Title = "无法打开浏览器",
                Content = new TextBlock { Text = GitHubUrl, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                PrimaryButtonText = "复制地址",
                CloseButtonText = "关闭",
                DefaultButton = FAContentDialogButton.Primary
            };
            if (await dialog.ShowAsync(this) != FAContentDialogResult.Primary) return;
            await CopyToClipboardAsync();
        }

        private async Task<bool> TryLaunchAsync()
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null) return false;
            try
            {
                return await launcher.LaunchUriAsync(new Uri(GitHubUrl));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task CopyToClipboardAsync()
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(GitHubUrl);
        }
    }
}