using Android.Media;
using Android.OS;
using Bdm.Core.Media;
using Java.Nio;

namespace Bdm.App.Droid;

/// <summary>
/// Merges YouTube's separate video (H.264/H.265 in MP4) and audio (AAC in M4A) streams into one MP4 using
/// Android's own MediaExtractor + MediaMuxer: no re-encoding and no FFmpeg. Samples are interleaved by
/// timestamp so memory stays flat even for long videos.
/// </summary>
public sealed class AndroidStreamMuxer : IStreamMuxer
{
    public bool IsAvailable => true;

    public bool Supports(string videoCodec, string audioCodec)
    {
        bool video = videoCodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) ||
                     videoCodec.StartsWith("hvc1", StringComparison.OrdinalIgnoreCase) ||
                     videoCodec.StartsWith("hev1", StringComparison.OrdinalIgnoreCase) ||
                     (videoCodec.StartsWith("av01", StringComparison.OrdinalIgnoreCase) && Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake);
        return video && audioCodec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase);
    }

    public Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct) => Task.Run(() =>
    {
        using var video = new MediaExtractor();
        using var audio = new MediaExtractor();
        video.SetDataSource(videoPath);
        audio.SetDataSource(audioPath);
        int vIn = FindTrack(video, "video/");
        int aIn = FindTrack(audio, "audio/");
        video.SelectTrack(vIn);
        audio.SelectTrack(aIn);

        var muxer = new MediaMuxer(outputPath, MuxerOutputType.Mpeg4);
        try
        {
            int vOut = muxer.AddTrack(video.GetTrackFormat(vIn));
            int aOut = muxer.AddTrack(audio.GetTrackFormat(aIn));
            muxer.Start();

            var buffer = ByteBuffer.AllocateDirect(4 * 1024 * 1024);
            var info = new MediaCodec.BufferInfo();
            bool vDone = false, aDone = false;
            while (!vDone || !aDone)
            {
                ct.ThrowIfCancellationRequested();
                // Write whichever track is behind in time (interleaving).
                bool takeVideo = !vDone && (aDone || video.SampleTime <= audio.SampleTime);
                var ex = takeVideo ? video : audio;
                int size = ex.ReadSampleData(buffer, 0);
                if (size < 0)
                {
                    if (takeVideo) vDone = true; else aDone = true;
                    continue;
                }
                var flags = (ex.SampleFlags & MediaExtractorSampleFlags.Sync) != 0 ? MediaCodecBufferFlags.KeyFrame : MediaCodecBufferFlags.None;
                info.Set(0, size, ex.SampleTime, flags);
                muxer.WriteSampleData(takeVideo ? vOut : aOut, buffer, info);
                ex.Advance();
            }
            muxer.Stop();
        }
        finally
        {
            muxer.Release();
        }
    }, ct);

    private static int FindTrack(MediaExtractor ex, string mimePrefix)
    {
        for (int i = 0; i < ex.TrackCount; i++)
            if (ex.GetTrackFormat(i).GetString(MediaFormat.KeyMime)?.StartsWith(mimePrefix, StringComparison.Ordinal) == true) return i;
        throw new InvalidOperationException($"No {mimePrefix.TrimEnd('/')} track found.");
    }
}
