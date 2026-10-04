using System.Reflection;
using System.Windows;
using Vid2Tiny.Services;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var version = VersionService.AppVersion;
            Title = $"vid2tiny v{version.Major}.{version.Minor}.{version.Build}";
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Dispose();
                vm.PersistSettings();
            }
        }
    }
}