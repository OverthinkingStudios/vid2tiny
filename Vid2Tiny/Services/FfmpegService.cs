using System.Globalization;
using System.Text;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class FfmpegService(ProcessRunner processRunner)
{
    public string BuildArguments(VideoJobParameters parameters)
    {
        var builder = new StringBuilder();

        builder.Append("-hide_banner -y ");
        builder.AppendFormat(CultureInfo.InvariantCulture, "-ss {0:0.###} ", parameters.StartTime.TotalSeconds);
        builder.AppendFormat(CultureInfo.InvariantCulture, "-to {0:0.###} ", parameters.EndTime.TotalSeconds);
        builder.AppendFormat("-i \"{0}\" ", parameters.InputPath);

        var filters = new List<string>();
        if (parameters.Crop is not null)
        {
            filters.Add(
                FormattableString.Invariant(
                    $"crop={Math.Round(parameters.Crop.Width)}:{Math.Round(parameters.Crop.Height)}:{Math.Round(parameters.Crop.X)}:{Math.Round(parameters.Crop.Y)}"));
        }

        if (parameters.ScaleWidth is > 0 || parameters.ScaleHeight is > 0)
        {
            var width = parameters.ScaleWidth is > 0 ? parameters.ScaleWidth.Value.ToString(CultureInfo.InvariantCulture) : "-2";
            var height = parameters.ScaleHeight is > 0 ? parameters.ScaleHeight.Value.ToString(CultureInfo.InvariantCulture) : "-2";
            filters.Add($"scale={width}:{height}");
        }

        if (filters.Count > 0)
        {
            builder.AppendFormat("-vf \"{0}\" ", string.Join(",", filters));
        }

        if (parameters.MuteAudio)
        {
            builder.Append("-an ");
        }
        else
        {
            builder.Append("-c:a aac -b:a 128k ");
        }

        builder.AppendFormat(CultureInfo.InvariantCulture, "-c:v libx264 -preset {0} -crf {1} ", parameters.EncoderPreset, parameters.Crf);
        builder.AppendFormat("\"{0}\"", parameters.OutputPath);

        return builder.ToString();
    }

    public async Task<int> ExecuteAsync(
        VideoJobParameters parameters,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default)
    {
        var args = BuildArguments(parameters);
        var result = await processRunner.RunAsync(
            "ffmpeg",
            args,
            onStdOut: onOutput,
            onStdErr: onError,
            cancellationToken: cancellationToken);

        return result.ExitCode;
    }
}
