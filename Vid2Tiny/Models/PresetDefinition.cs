namespace Vid2Tiny.Models;

public sealed class PresetDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int DefaultCrf { get; init; }

    public required string EncoderPreset { get; init; }
}
