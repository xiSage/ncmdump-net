using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DesktopApp.ViewModels;
using DesktopApp.Views;

namespace DesktopApp
{
    /// <summary>
    ///   Resolves the View for a ViewModel. The mapping is a plain, enumerable
    ///   switch — statically visible to trimming/AOT, so no source generator is
    ///   needed (a one-to-one mapping for a single ViewModel never justified one).
    /// </summary>
    public class ViewLocator : IDataTemplate
    {

        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            if (param is not ViewModelBase vm)
                return new TextBlock { Text = "Unknown: " + param.GetType().Name };

            return vm switch
            {
                MainWindowViewModel => new MainWindowView(),
                _ => new TextBlock { Text = "Not Found: " + vm.GetType().Name }
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}