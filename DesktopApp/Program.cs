using Avalonia;
using Avalonia.Media;
using System;

namespace DesktopApp
{
    internal sealed class Program
    {
        // 内嵌 Inter 字体集合的完整引用（集合键 + 族名）。只写 "Inter" 解析不到内嵌集合，
        // 会在启动时抛出 "Could not create glyphTypeface"。
        private const string DefaultFontFamilyName = "fonts:Inter#Inter";

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            _ = BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                        .UsePlatformDetect()
                        .WithInterFont()
                        // 显式指定默认字体族，不依赖平台默认值：Linux 上该默认值由 fontconfig 决定，
                        // 可能落在位图字体（如 Windows 字体目录里的 8514fix）或与预期不符的族名上。
                        // Inter 内嵌在本程序内，各平台都能解析；中日韩字形交给平台逐字回退。
                        .With(new FontManagerOptions
                        {
                            DefaultFamilyName = DefaultFontFamilyName,
                        })
                        .LogToTrace()
#if DEBUG
                        .WithDeveloperTools()
#endif
                        ;
        }
    }
}