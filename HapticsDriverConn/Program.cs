using System.Diagnostics;
using Nefarius.ViGEm.Client;
using ScottPlot;

var events = new List<(double t, byte large, byte small)>();
var gate = new object();
var sw = Stopwatch.StartNew();
bool finished = false;

using var client = new ViGEmClient();
var pad = client.CreateXbox360Controller();

double? rumbleStart = null, lastRumbleEnd = null;
int lastLarge = 0, lastSmall = 0;

pad.FeedbackReceived += (s, e) =>
{
    double t = sw.Elapsed.TotalSeconds;
    lock (gate)
    {
        events.Add((t, e.LargeMotor, e.SmallMotor));

        bool on = e.LargeMotor > 0 || e.SmallMotor > 0;
        bool changed = e.LargeMotor != lastLarge || e.SmallMotor != lastSmall;
        lastLarge = e.LargeMotor;
        lastSmall = e.SmallMotor;

        if (on && rumbleStart == null)
        {
            string gap = lastRumbleEnd != null ? $"  (gap since last: {(t - lastRumbleEnd.Value) * 1000:F0} ms)" : "";
            Console.WriteLine($"[{t,8:F3}s] RUMBLE START  large={e.LargeMotor,3}  small={e.SmallMotor,3}{gap}");
            rumbleStart = t;
        }
        else if (on && changed)
        {
            Console.WriteLine($"[{t,8:F3}s]   change      large={e.LargeMotor,3}  small={e.SmallMotor,3}");
        }
        else if (!on && rumbleStart != null)
        {
            Console.WriteLine($"[{t,8:F3}s] RUMBLE STOP   lasted {(t - rumbleStart.Value) * 1000:F0} ms");
            lastRumbleEnd = t;
            rumbleStart = null;
        }
    }
};

pad.Connect();
sw.Restart();

var quit = new ManualResetEventSlim();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; quit.Set(); };
AppDomain.CurrentDomain.ProcessExit += (s, e) => Finish();

Console.WriteLine("Virtual pad connected. Launch your game. Press Ctrl+C to stop and save the graph.");
quit.Wait();
Finish();

void Finish()
{
    lock (gate)
    {
        if (finished) return;
        finished = true;

        double endT = sw.Elapsed.TotalSeconds;
        try { pad.Disconnect(); } catch { }

        if (events.Count == 0)
        {
            Console.WriteLine("No rumble received, nothing to graph.");
            return;
        }

        string path = Path.GetFullPath($"rumble_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        SaveGraph(events, endT, path);
        Console.WriteLine($"Saved graph to {path}");
    }
}

static void SaveGraph(List<(double t, byte large, byte small)> ev, double endT, string path)
{
    var lx = new List<double>(); var ly = new List<double>();
    var sx = new List<double>(); var sy = new List<double>();
    double pl = 0, ps = 0;
    lx.Add(0); ly.Add(0); sx.Add(0); sy.Add(0);
    foreach (var (t, l, s) in ev)
    {
        lx.Add(t); ly.Add(pl); lx.Add(t); ly.Add(l);
        sx.Add(t); sy.Add(ps); sx.Add(t); sy.Add(s);
        pl = l; ps = s;
    }
    lx.Add(endT); ly.Add(pl);
    sx.Add(endT); sy.Add(ps);

    var rumbles = new List<(double start, double end, int peak)>();
    double? start = null; int peak = 0;
    foreach (var (t, l, s) in ev)
    {
        bool on = l > 0 || s > 0;
        if (on && start == null) { start = t; peak = 0; }
        if (on) peak = Math.Max(peak, Math.Max(l, s));
        if (!on && start != null) { rumbles.Add((start.Value, t, peak)); start = null; }
    }
    if (start != null) rumbles.Add((start.Value, endT, peak));

    var plot = new Plot();
    plot.Title("Controller rumble over time");
    plot.XLabel("Time (s)");
    plot.YLabel("Motor intensity (0-255)");

    var large = plot.Add.Scatter(lx.ToArray(), ly.ToArray());
    large.LegendText = "Large motor (low freq)";
    large.Color = Colors.Blue;
    large.MarkerSize = 0;
    large.FillY = true;
    large.FillYColor = Colors.Blue.WithAlpha(0.25);

    var small = plot.Add.Scatter(sx.ToArray(), sy.ToArray());
    small.LegendText = "Small motor (high freq)";
    small.Color = Colors.Red;
    small.MarkerSize = 0;
    small.FillY = true;
    small.FillYColor = Colors.Red.WithAlpha(0.25);

    if (rumbles.Count <= 40)
    {
        for (int i = 0; i < rumbles.Count; i++)
        {
            var r = rumbles[i];
            var d = plot.Add.Text($"{(r.end - r.start) * 1000:F0} ms", (r.start + r.end) / 2, r.peak + 6);
            d.LabelFontSize = 11;
            d.LabelAlignment = Alignment.LowerCenter;

            if (i > 0)
            {
                double gs = rumbles[i - 1].end, ge = r.start;
                var g = plot.Add.Text($"gap {(ge - gs) * 1000:F0} ms", (gs + ge) / 2, 8);
                g.LabelFontSize = 10;
                g.LabelFontColor = Colors.Gray;
                g.LabelAlignment = Alignment.LowerCenter;
            }
        }
    }

    plot.Axes.SetLimits(0, endT, 0, 290);
    plot.ShowLegend();
    plot.SavePng(path, 1800, 700);
}