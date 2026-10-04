using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is TextBoxBase || DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var command = e.Key switch
            {
                Key.OemComma => vm.Preview.StepFrameBackCommand,
                Key.OemPeriod => vm.Preview.StepFrameForwardCommand,
                Key.N => vm.Preview.NextClipCommand,
                _ => null
            };

            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
                e.Handled = true;
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase { IsMouseOver: false })
            {
                Focus();
            }
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
