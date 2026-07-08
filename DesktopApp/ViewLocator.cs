using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DesktopApp.ViewModels;
using DesktopApp.Views;

namespace DesktopApp
{
    public class ViewLocator : IDataTemplate
    {

        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            if (param is not ViewModelBase vm)
                return new TextBlock { Text = "Unknown: " + param.GetType().Name };

            var view = ViewMapping.ResolveView(vm);
            return view ?? new TextBlock { Text = "Not Found: " + vm.GetType().Name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
