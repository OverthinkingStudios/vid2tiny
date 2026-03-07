using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vid2Tiny.Models;

namespace Vid2Tiny.ViewModels;

public partial class EncodeOptionsViewModel : ObservableObject
{
    public ObservableCollection<PresetDefinition> Presets { get; } =
    [
        new()
        {
            Id = "high-quality",
            Name = "High Quality",
            Description = "Large output, better quality.",
            DefaultCrf = 19,
            EncoderPreset = "slow"
        },
        new()
        {
            Id = "balanced",
            Name = "Balanced",
            Description = "Good size and quality balance.",
            DefaultCrf = 24,
            EncoderPreset = "medium"
        },
        new()
        {
            Id = "small-file",
            Name = "Small File",
            Description = "Smaller output for sharing quickly.",
            DefaultCrf = 29,
            EncoderPreset = "faster"
        }
    ];

    [ObservableProperty]
    private PresetDefinition? selectedPreset;

    [ObservableProperty]
    private bool muteAudio;

    [ObservableProperty]
    private int crf = 24;

    [ObservableProperty]
    private string encoderPreset = "medium";

    [ObservableProperty]
    private int? scaleWidth;

    [ObservableProperty]
    private int? scaleHeight;

    partial void OnSelectedPresetChanged(PresetDefinition? value)
    {
        if (value is null)
        {
            return;
        }

        Crf = value.DefaultCrf;
        EncoderPreset = value.EncoderPreset;
    }
}
