using System.Windows;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Services;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settingsService = new SettingsService();
            var settings = await settingsService.LoadAsync();
            var logService = new LogService();
            var galleryService = new GalleryService();
            var processRunner = new ProcessRunner();
            var probeService = new MediaProbeService(processRunner);
            var ffmpegService = new FfmpegService(processRunner);

            var mainViewModel = new MainWindowViewModel(
                settingsService,
                settings,
                galleryService,
                probeService,
                ffmpegService,
                logService);

            await mainViewModel.InitializeAsync();

            var window = new MainWindow
            {
                DataContext = mainViewModel
            };

            window.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (Current.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                await vm.PersistSettingsAsync();
            }

            base.OnExit(e);
        }
    }
}
