using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using BpstVideoDownloader.Core;

namespace BpstVideoDownloader.App.Controls;

/// <summary>Draws each connection's byte range: finished parts, active parts and the gaps still to fetch.</summary>
public sealed class SegmentBar : FrameworkElement
{
    private static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)));
    private static readonly Brush Done = Freeze(new SolidColorBrush(Color.FromRgb(20, 184, 166)));
    private static readonly Brush Active = Freeze(new SolidColorBrush(Color.FromRgb(37, 99, 235)));
    private static readonly Pen Divider = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1));

    private IReadOnlyList<SegmentInfo> _segments = [];
    private long _total;

    public void Update(IReadOnlyList<SegmentInfo> segments, long total)
    {
        _segments = segments;
        _total = total;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), 3, 3);
        if (_total <= 0 || _segments.Count == 0) return;

        foreach (var s in _segments)
        {
            double x = s.Start * w / _total;
            double dw = Math.Min(s.Downloaded, Math.Max(0, s.End - s.Start + 1)) * w / _total;
            if (dw > 0) dc.DrawRectangle(s.Active ? Active : Done, null, new Rect(x, 0, Math.Max(dw, 0.5), h));
            if (s.Start > 0) dc.DrawLine(Divider, new Point(x, 0), new Point(x, h));
        }
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
