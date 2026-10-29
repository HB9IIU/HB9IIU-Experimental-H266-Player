using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QocastPlayer
{
    // The colours and fonts of the two information rows (shared by InfoStrip and SignalBar).
    internal static class Look
    {
        public static readonly Color Background = Color.FromArgb(10, 10, 10);
        public static readonly Color Rule = Color.FromArgb(34, 34, 34);
        public static readonly Color Text = Color.FromArgb(224, 229, 234);
        public static readonly Color Muted = Color.FromArgb(128, 137, 146);
        public static readonly Color Amber = Color.FromArgb(255, 176, 0);
        public static readonly Color Green = Color.FromArgb(61, 220, 132);
        public static readonly Color Red = Color.FromArgb(255, 99, 90);
        public static readonly Color BadgeGrey = Color.FromArgb(52, 58, 64);

        public static readonly Font Value = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Label = new Font("Segoe UI", 7.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.5f);
        public static readonly Font Badge = new Font("Segoe UI Semibold", 8f);

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                              | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

        public static int Width(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Flags).Width;
        }

        // draws text with its left edge at x, centred on the row; returns its width
        public static int Draw(Graphics g, string text, Font font, Color color, int x, int rowHeight)
        {
            int width = Width(text, font);
            TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, width + 2, rowHeight), color, Flags);
            return width;
        }
    }

    // One piece of the signal row: SR 500 kS/s, MER 7.2 dB, ...
    public struct SignalSegment
    {
        public string Label;
        public string Value;
        public Color ValueColor;
    }

    // The thin strip above the bottom bar: what is playing (codec badge and picture size), the station,
    // the waiting message until the first picture, and the processor load on the right.
    public sealed class InfoStrip : System.Windows.Forms.Control
    {
        private string _codec, _size, _station, _status;
        private int? _pc, _decoder;

        public InfoStrip()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Look.Background;
        }

        public string Codec { set { Change(ref _codec, value); } }
        public string PictureSize { set { Change(ref _size, value); } }
        public string Station { set { Change(ref _station, value); } }
        public string Status { set { Change(ref _status, value); } }

        public void SetLoad(int? pc, int? decoder)
        {
            if (pc != _pc || decoder != _decoder)
            {
                _pc = pc;
                _decoder = decoder;
                Invalidate();
            }
        }

        private void Change(ref string field, string value)
        {
            if (string.IsNullOrEmpty(value))
                value = null;
            if (field != value)
            {
                field = value;
                Invalidate();
            }
        }

        private static Color LoadColor(int percent)
        {
            return percent >= 90 ? Look.Red : percent >= 70 ? Look.Amber : Look.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Look.Background);
            using (var rule = new Pen(Look.Rule))
                g.DrawLine(rule, 0, 0, Width, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Height;
            int left = 8;

            // left: codec badge (green for H.266) and the picture size
            if (_codec != null)
            {
                bool vvc = _codec == "H.266";
                int textWidth = Look.Width(_codec, Look.Badge);
                var badge = new Rectangle(left, (h - 15) / 2 + 1, textWidth + 12, 15);
                using (var path = Rounded(badge, 4))
                using (var fill = new SolidBrush(vvc ? Look.Green : Look.BadgeGrey))
                    g.FillPath(fill, path);
                Look.Draw(g, _codec, Look.Badge, vvc ? Color.FromArgb(6, 28, 16) : Look.Text, badge.X + 6, h);
                left = badge.Right + 8;
                if (_size != null)
                    left += Look.Draw(g, _size.Replace('x', '×'), Look.Small, Look.Muted, left, h) + 8;
            }

            // right: processor load, "CPU  PC 39 %   Decoder 2 %"
            int rightWidth = 0;
            var parts = new List<Tuple<string, Font, Color>>();
            parts.Add(Tuple.Create("CPU", Look.Label, Look.Muted));
            if (_pc.HasValue)
            {
                parts.Add(Tuple.Create("PC", Look.Label, Look.Muted));
                parts.Add(Tuple.Create(_pc.Value + " %", Look.SmallBold, LoadColor(_pc.Value)));
            }
            if (_decoder.HasValue)
            {
                parts.Add(Tuple.Create("DECODER", Look.Label, Look.Muted));
                parts.Add(Tuple.Create(_decoder.Value + " %", Look.SmallBold, LoadColor(_decoder.Value)));
            }
            const int gap = 5;
            foreach (var part in parts)
                rightWidth += Look.Width(part.Item1, part.Item2) + gap;
            rightWidth -= gap;
            int x = Math.Max(left + 8, Width - 8 - rightWidth);
            int rightEdge = x;
            foreach (var part in parts)
                x += Look.Draw(g, part.Item1, part.Item2, part.Item3, x, h) + gap;

            // middle: the station, then the waiting message in amber
            string station = _station ?? "";
            int free = rightEdge - left - 16;
            if (free > 20 && (station.Length > 0 || _status != null))
            {
                int stationWidth = station.Length > 0 ? Look.Width(station, Look.SmallBold) : 0;
                int statusWidth = _status != null ? Look.Width(_status, Look.Small) : 0;
                int between = stationWidth > 0 && statusWidth > 0 ? 14 : 0;
                int total = stationWidth + between + statusWidth;
                if (total > free)
                {                                           // too narrow: the waiting message goes first
                    statusWidth = 0;
                    between = 0;
                    total = stationWidth;
                }
                int start = left + (rightEdge - left - total) / 2;
                if (stationWidth > 0 && stationWidth <= free)
                    start += Look.Draw(g, station, Look.SmallBold, Look.Text, start, h) + between;
                if (statusWidth > 0)
                    Look.Draw(g, _status, Look.Small, Look.Amber, start, h);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // The signal values in the middle of the bottom bar: a muted label and a bright value each,
    // separated by thin rules. Pieces that do not fit are left out from the end.
    public sealed class SignalBar : System.Windows.Forms.Control
    {
        private List<SignalSegment> _segments = new List<SignalSegment>();

        public SignalBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(14, 14, 14);
        }

        public void SetSegments(List<SignalSegment> segments)
        {
            bool same = segments.Count == _segments.Count;
            for (int i = 0; same && i < segments.Count; i++)
                same = segments[i].Label == _segments[i].Label && segments[i].Value == _segments[i].Value
                       && segments[i].ValueColor == _segments[i].ValueColor;
            if (!same)
            {
                _segments = segments;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int h = Height;
            const int labelGap = 5, between = 22;

            var widths = new List<int>();
            foreach (SignalSegment s in _segments)
                widths.Add(Look.Width(s.Label, Look.Label) + labelGap + Look.Width(s.Value, Look.Value));

            int count = _segments.Count;
            int total = 0;
            for (; count > 0; count--)
            {
                total = -between;
                for (int i = 0; i < count; i++)
                    total += widths[i] + between;
                if (total <= Width - 8)
                    break;
            }
            if (count == 0)
                return;

            int x = (Width - total) / 2;
            using (var rule = new Pen(Color.FromArgb(46, 46, 46)))
                for (int i = 0; i < count; i++)
                {
                    SignalSegment s = _segments[i];
                    x += Look.Draw(g, s.Label, Look.Label, Look.Muted, x, h) + labelGap;
                    x += Look.Draw(g, s.Value, Look.Value, s.ValueColor, x, h);
                    if (i < count - 1)
                    {
                        int rx = x + between / 2;
                        g.DrawLine(rule, rx, h / 2 - 6, rx, h / 2 + 6);
                        x += between;
                    }
                }
        }
    }
}
