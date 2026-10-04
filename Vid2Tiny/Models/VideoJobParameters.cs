namespace Vid2Tiny.Models;

public sealed record VideoJobParameters(Clip Clip, EncodePlan Plan, string OutputPath);
