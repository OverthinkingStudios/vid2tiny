using System.Windows;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if(DataContext is MainWindowViewModel vm)
            {
                vm.PersistSettings();
            }
        }
    }
}