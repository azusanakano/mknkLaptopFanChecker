using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Mknk.LaptopFanChecker
{
    public sealed class TelemetryChart : Control
    {
        private readonly object _sync = new object();
        private List<TelemetrySample> _samples = new List<TelemetrySample>();

        public TelemetryChart()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 25, 35);
            ForeColor = Color.FromArgb(210, 220, 235);
            MinimumSize = new Size(300, 180);
        }

        public void SetSamples(IEnumerable<TelemetrySample> samples)
        {
            lock (_sync)
                _samples = samples.ToList();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            Rectangle plot = new Rectangle(54, 28, Math.Max(10, Width - 76), Math.Max(10, Height - 66));
            using (Pen border = new Pen(Color.FromArgb(70, 85, 105)))
                e.Graphics.DrawRectangle(border, plot);

            List<TelemetrySample> samples;
            lock (_sync)
                samples = _samples.ToList();

            DrawGrid(e.Graphics, plot);
            DrawLegend(e.Graphics);
            if (samples.Count < 2)
            {
                using (Brush brush = new SolidBrush(Color.FromArgb(150, 165, 185)))
                    e.Graphics.DrawString("測定データを待っています", Font, brush, plot.Left + 18, plot.Top + 22);
                return;
            }

            double minX = samples.Min(s => s.ElapsedSeconds);
            double maxX = samples.Max(s => s.ElapsedSeconds);
            if (maxX - minX < 1.0)
                maxX = minX + 1.0;
            double maxFan = samples.Where(s => s.FanRpm.HasValue).Select(s => s.FanRpm.Value).DefaultIfEmpty(0.0).Max();
            double fanScale = Math.Max(2000.0, Math.Ceiling(maxFan / 1000.0) * 1000.0);
            double maxClock = samples.Where(s => s.CpuClockGHz.HasValue).Select(s => s.CpuClockGHz.Value).DefaultIfEmpty(0.0).Max();
            double clockScale = Math.Max(5.0, Math.Ceiling(maxClock));

            DrawPhaseBands(e.Graphics, plot, samples, minX, maxX);
            DrawSeries(e.Graphics, plot, samples, minX, maxX, s => s.TemperatureC, 20.0, 110.0, Color.FromArgb(71, 208, 255), 2.6f);
            DrawSeries(e.Graphics, plot, samples, minX, maxX, s => s.CpuLoadPercent, 0.0, 100.0, Color.FromArgb(255, 174, 66), 1.8f);
            DrawSeries(e.Graphics, plot, samples, minX, maxX, s => s.FanRpm, 0.0, fanScale, Color.FromArgb(217, 111, 255), 2.0f);
            DrawSeries(e.Graphics, plot, samples, minX, maxX, s => s.CpuClockGHz, 0.0, clockScale, Color.FromArgb(110, 220, 160), 2.0f);

            using (Brush axisBrush = new SolidBrush(Color.FromArgb(155, 170, 190)))
            {
                e.Graphics.DrawString("110℃", Font, axisBrush, 5, plot.Top - 6);
                e.Graphics.DrawString("65℃", Font, axisBrush, 12, plot.Top + plot.Height / 2 - 7);
                e.Graphics.DrawString("20℃", Font, axisBrush, 12, plot.Bottom - 8);
                string fanText = String.Format("Fan上限 {0:0} RPM", fanScale);
                SizeF size = e.Graphics.MeasureString(fanText, Font);
                e.Graphics.DrawString(fanText, Font, axisBrush, plot.Right - size.Width, plot.Bottom + 8);
                e.Graphics.DrawString(String.Format("{0:0}秒", maxX - minX), Font, axisBrush, plot.Left, plot.Bottom + 8);
                e.Graphics.DrawString(String.Format("速度 0–{0:0} GHz", clockScale), Font, axisBrush, plot.Left + 80, plot.Bottom + 8);
            }
        }

        private void DrawGrid(Graphics graphics, Rectangle plot)
        {
            using (Pen pen = new Pen(Color.FromArgb(42, 53, 69)))
            {
                for (int i = 1; i < 4; i++)
                {
                    int y = plot.Top + plot.Height * i / 4;
                    graphics.DrawLine(pen, plot.Left, y, plot.Right, y);
                }
                for (int i = 1; i < 6; i++)
                {
                    int x = plot.Left + plot.Width * i / 6;
                    graphics.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                }
            }
        }

        private void DrawLegend(Graphics graphics)
        {
            DrawLegendItem(graphics, 58, 8, Color.FromArgb(71, 208, 255), "CPU温度");
            DrawLegendItem(graphics, 150, 8, Color.FromArgb(255, 174, 66), "CPU負荷");
            DrawLegendItem(graphics, 242, 8, Color.FromArgb(217, 111, 255), "ファンRPM");
            DrawLegendItem(graphics, 350, 8, Color.FromArgb(110, 220, 160), "CPU実働速度");
        }

        private void DrawLegendItem(Graphics graphics, int x, int y, Color color, string text)
        {
            using (Pen pen = new Pen(color, 3f))
                graphics.DrawLine(pen, x, y + 7, x + 18, y + 7);
            using (Brush brush = new SolidBrush(ForeColor))
                graphics.DrawString(text, Font, brush, x + 23, y);
        }

        private static void DrawPhaseBands(Graphics graphics, Rectangle plot, IList<TelemetrySample> samples, double minX, double maxX)
        {
            TestPhase[] phases = new[] { TestPhase.Idle, TestPhase.Load, TestPhase.Cooldown };
            Color[] colors = new[]
            {
                Color.FromArgb(18, 84, 180, 140),
                Color.FromArgb(28, 255, 145, 70),
                Color.FromArgb(18, 80, 150, 255)
            };

            for (int i = 0; i < phases.Length; i++)
            {
                List<TelemetrySample> phaseSamples = samples.Where(s => s.Phase == phases[i]).ToList();
                if (phaseSamples.Count == 0)
                    continue;
                double start = phaseSamples.Min(s => s.ElapsedSeconds);
                double end = phaseSamples.Max(s => s.ElapsedSeconds);
                int x1 = plot.Left + (int)((start - minX) / (maxX - minX) * plot.Width);
                int x2 = plot.Left + (int)((end - minX) / (maxX - minX) * plot.Width);
                using (Brush brush = new SolidBrush(colors[i]))
                    graphics.FillRectangle(brush, x1, plot.Top, Math.Max(2, x2 - x1), plot.Height);
            }
        }

        private static void DrawSeries(
            Graphics graphics,
            Rectangle plot,
            IList<TelemetrySample> samples,
            double minX,
            double maxX,
            Func<TelemetrySample, double?> selector,
            double minY,
            double maxY,
            Color color,
            float width)
        {
            List<PointF> points = new List<PointF>();
            using (Pen pen = new Pen(color, width))
            {
                pen.LineJoin = LineJoin.Round;
                foreach (TelemetrySample sample in samples)
                {
                    double? raw = selector(sample);
                    if (!raw.HasValue)
                    {
                        DrawSegment(graphics, pen, points);
                        points.Clear();
                        continue;
                    }
                    double yValue = Math.Max(minY, Math.Min(maxY, raw.Value));
                    float x = plot.Left + (float)((sample.ElapsedSeconds - minX) / (maxX - minX) * plot.Width);
                    float y = plot.Bottom - (float)((yValue - minY) / (maxY - minY) * plot.Height);
                    points.Add(new PointF(x, y));
                }
                DrawSegment(graphics, pen, points);
            }
        }

        private static void DrawSegment(Graphics graphics, Pen pen, List<PointF> points)
        {
            if (points.Count >= 2)
                graphics.DrawLines(pen, points.ToArray());
        }
    }
}
