using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public sealed class SourceVideoViewModel : FileItemViewModel
{
    public SourceVideoViewModel(SourceVideo video, SourceTagViewModel tag, LogService logService, Action<FileItemViewModel> openInEditor)
        : base(video.FilePath, video.CreationTimeUtc, logService, openInEditor)
    {
        Tag = tag;
        Update(video.LastWriteTimeUtc, video.SizeBytes);
    }

    public SourceTagViewModel Tag { get; }
}
