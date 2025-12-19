using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Threading;

namespace AlephTavCutVideoEditorApp
{
    public partial class MainWindow : Window
    {
        private string? _videoPath;
        private readonly List<(TimeSpan Start, TimeSpan End)> _cuts = new();
        private DispatcherTimer? _timer;
        private CancellationTokenSource? _exportCts;

        public MainWindow()
        {
            InitializeComponent();
            MediaPreview.MediaOpened += MediaPreview_MediaOpened;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (MediaPreview.NaturalDuration.HasTimeSpan)
                TxtPosition.Text = MediaPreview.Position.ToString(@"hh\:mm\:ss");
        }

        private void MediaPreview_MediaOpened(object? sender, RoutedEventArgs e)
        {
            _timer?.Start();
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.wmv|All files|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                _videoPath = dlg.FileName;
                MediaPreview.Source = new Uri(_videoPath);
                MediaPreview.Play();
                MediaPreview.Pause();
                TxtStatus.Text = $"Loaded: {Path.GetFileName(_videoPath)}";
            }
        }

        private void BtnBrowseFf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe" };
            if (dlg.ShowDialog(this) == true)
            {
                TxtFfmpegPath.Text = dlg.FileName;
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            MediaPreview.Play();
            TxtStatus.Text = "Playing";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            MediaPreview.Pause();
            TxtStatus.Text = "Paused";
        }

        private void BtnAddCut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = ParseTimestamp(TxtStart.Text.Trim());
                var t = ParseTimestamp(TxtEnd.Text.Trim());
                if (t <= s)
                {
                    MessageBox.Show(this, "End must be later than Start.", "Invalid range", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _cuts.Add((s, t));
                NormalizeCuts();
                RefreshCutsList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't parse times: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveCut_Click(object sender, RoutedEventArgs e)
        {
            if (ListCuts.SelectedIndex >= 0)
            {
                _cuts.RemoveAt(ListCuts.SelectedIndex);
                RefreshCutsList();
            }
        }

        private void BtnClearCuts_Click(object sender, RoutedEventArgs e)
        {
            _cuts.Clear();
            RefreshCutsList();
        }

        private void RefreshCutsList()
        {
            ListCuts.Items.Clear();
            foreach (var c in _cuts.OrderBy(x => x.Start))
                ListCuts.Items.Add($"{c.Start:hh\\:mm\\:ss} → {c.End:hh\\:mm\\:ss}");
        }

        private static TimeSpan ParseTimestamp(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Empty timestamp");
            // Accept formats: hh:mm:ss(.fff) or seconds with decimal
            if (text.Contains(":"))
            {
                return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            }
            else
            {
                var sec = double.Parse(text, CultureInfo.InvariantCulture);
                return TimeSpan.FromSeconds(sec);
            }
        }

        private void NormalizeCuts()
        {
            if (_cuts.Count == 0) return;
            var ordered = _cuts.OrderBy(x => x.Start).ToList();
            var merged = new List<(TimeSpan Start, TimeSpan End)>();
            var cur = ordered[0];
            foreach (var c in ordered.Skip(1))
            {
                if (c.Start <= cur.End)
                {
                    cur = (cur.Start, c.End > cur.End ? c.End : cur.End);
                }
                else
                {
                    merged.Add(cur);
                    cur = c;
                }
            }
            merged.Add(cur);
            _cuts.Clear();
            _cuts.AddRange(merged);
        }

        internal static List<(TimeSpan Start, TimeSpan End)> ComputeKeepSegments(TimeSpan duration, List<(TimeSpan Start, TimeSpan End)> cuts)
        {
            var keep = new List<(TimeSpan Start, TimeSpan End)>();
            var last = TimeSpan.Zero;
            foreach (var c in cuts)
            {
                if (c.Start > last)
                    keep.Add((last, c.Start));
                last = c.End;
            }
            if (last < duration)
                keep.Add((last, duration));
            // remove zero-lengths
            return keep.Where(k => k.End > k.Start).ToList();
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_videoPath) || !File.Exists(_videoPath))
            {
                MessageBox.Show(this, "Load a video file first.", "No video", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!MediaPreview.NaturalDuration.HasTimeSpan)
            {
                MessageBox.Show(this, "Wait until video is loaded (media duration must be known).", "Not ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var duration = MediaPreview.NaturalDuration.TimeSpan;
            // compute keep segments from cuts
            var keep = ComputeKeepSegments(duration, _cuts.OrderBy(x => x.Start).ToList());
            if (keep.Count == 0)
            {
                MessageBox.Show(this, "The whole video would be removed. Cancel or adjust cuts.", "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sfd = new SaveFileDialog { Filter = "MP4 video|*.mp4|All files|*.*", FileName = Path.GetFileNameWithoutExtension(_videoPath) + "_edited.mp4" };
            if (sfd.ShowDialog(this) != true) return;
            var output = sfd.FileName;

            Progress.Visibility = Visibility.Visible;
            Progress.Value = 0;
            TxtStatus.Text = "Exporting...";

            var progressReporter = new Progress<double>(p =>
            {
                Progress.Visibility = Visibility.Visible;
                Progress.Value = Math.Min(100, (int)(p * 100));
                TxtStatus.Text = $"Exporting... {Math.Min(100, (int)(p * 100))}%";
            });

            try
            {
                _exportCts = new CancellationTokenSource();
                var token = _exportCts.Token;
                bool precise = ChkPreciseTrim.IsChecked == true;
                await Task.Run(() => ExportUsingFfmpegWithProgress(_videoPath!, keep, TxtFfmpegPath.Text.Trim(), output, progressReporter, token, precise));
                Progress.Value = 100;
                TxtStatus.Text = "Export completed: " + output;
                MessageBox.Show(this, "Export finished successfully.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Export canceled.";
                MessageBox.Show(this, "Export canceled.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Export failed.";
                MessageBox.Show(this, "Export failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _exportCts = null;
                Progress.Visibility = Visibility.Collapsed;
                Progress.Value = 0;
            }
        }

        private static void RunProcess(string exe, string arguments, Action<double>? progressCallback = null, double? expectedDurationSeconds = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg process");

            p.OutputDataReceived += (s, e) => { /* ignore stdout */ };
            p.ErrorDataReceived += (s, e) =>
            {
                if (e?.Data == null) return;
                var line = e.Data;
                try
                {
                    var idx = line.IndexOf("time=", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && expectedDurationSeconds.HasValue && progressCallback != null)
                    {
                        var after = line.Substring(idx + 5);
                        var token = after.Split(' ')[0].Trim();
                        TimeSpan ts;
                        if (token.Contains(":"))
                            ts = TimeSpan.Parse(token, CultureInfo.InvariantCulture);
                        else
                            ts = TimeSpan.FromSeconds(double.Parse(token, CultureInfo.InvariantCulture));
                        var pct = Math.Max(0.0, Math.Min(1.0, ts.TotalSeconds / expectedDurationSeconds.Value));
                        try { progressCallback(pct); } catch { }
                    }
                }
                catch { /* ignore parse errors */ }
            };

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed (exit code: " + p.ExitCode + ")");
        }

        private static void ExportUsingFfmpegWithProgress(string inputPath, List<(TimeSpan Start, TimeSpan End)> keepSegments, string ffmpegPath, string outputPath, IProgress<double>? progress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath)) ffmpegPath = "ffmpeg.exe";
            if (!File.Exists(ffmpegPath) && !IsInPath(ffmpegPath))
                throw new FileNotFoundException("ffmpeg executable not found. Put ffmpeg.exe in the app folder or set its path.");

            var tmp = Path.Combine(Path.GetTempPath(), "AlephTavCut_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var parts = new List<string>();

            try
            {
                int i = 0;
                var total = Math.Max(1, keepSegments.Count);
                foreach (var seg in keepSegments)
                {
                    token.ThrowIfCancellationRequested();
                    var part = Path.Combine(tmp, $"part_{i}.mp4");
                    var startS = seg.Start.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                    var durSeconds = (seg.End - seg.Start).TotalSeconds;
                    var durS = durSeconds.ToString(CultureInfo.InvariantCulture);
                    var args = $"-y -ss {startS} -i \"{inputPath}\" -t {durS} -c copy \"{part}\"";

                    RunProcess(ffmpegPath, args, partPct =>
                    {
                        var overall = ((i + partPct) / total) * 0.95; // reserve last 5% for concat
                        try { progress?.Report(overall); } catch { }
                    }, durSeconds);

                    parts.Add(part);
                    i++;
                }

                token.ThrowIfCancellationRequested();
                var listFile = Path.Combine(tmp, "concat.txt");
                using (var sw = new StreamWriter(listFile))
                {
                    foreach (var p in parts)
                        sw.WriteLine($"file '{p.Replace("'", "'\\''")}'");
                }

                var argsConcat = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"";
                progress?.Report(0.96);
                RunProcess(ffmpegPath, argsConcat, _ => { progress?.Report(0.98); }, null);
                progress?.Report(1.0);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        // New overload: supports precise trimming (re-encode) when requested.
        internal static void ExportUsingFfmpegWithProgress(string inputPath, List<(TimeSpan Start, TimeSpan End)> keepSegments, string ffmpegPath, string outputPath, IProgress<double>? progress, CancellationToken token, bool precise)
        {
            if (!precise)
            {
                // fallback to existing implementation (fast copy)
                ExportUsingFfmpegWithProgress(inputPath, keepSegments, ffmpegPath, outputPath, progress, token);
                return;
            }

            // Precise mode: re-encode each segment for frame-accurate trimming.
            if (string.IsNullOrWhiteSpace(ffmpegPath)) ffmpegPath = "ffmpeg.exe";
            if (!File.Exists(ffmpegPath) && !IsInPath(ffmpegPath))
                throw new FileNotFoundException("ffmpeg executable not found. Put ffmpeg.exe in the app folder or set its path.");

            var tmp = Path.Combine(Path.GetTempPath(), "AlephTavCut_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var parts = new List<string>();

            try
            {
                int i = 0;
                var total = Math.Max(1, keepSegments.Count);
                foreach (var seg in keepSegments)
                {
                    token.ThrowIfCancellationRequested();
                    var part = Path.Combine(tmp, $"part_{i}.mp4");
                    var startS = seg.Start.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                    var durSeconds = (seg.End - seg.Start).TotalSeconds;
                    var durS = durSeconds.ToString(CultureInfo.InvariantCulture);

                    // Seek after input (-i) for accurate trimming, re-encode to ensure frame-accuracy.
                    var args = $"-y -i \"{inputPath}\" -ss {startS} -t {durS} -c:v libx264 -preset veryfast -crf 23 -c:a aac \"{part}\"";

                    RunProcess(ffmpegPath, args, partPct =>
                    {
                        var overall = ((i + partPct) / total) * 0.95; // reserve last 5% for concat
                        try { progress?.Report(overall); } catch { }
                    }, durSeconds);

                    parts.Add(part);
                    i++;
                }

                token.ThrowIfCancellationRequested();
                var listFile = Path.Combine(tmp, "concat.txt");
                using (var sw = new StreamWriter(listFile, false, System.Text.Encoding.ASCII))
                {
                    foreach (var p in parts)
                        sw.WriteLine($"file '{p.Replace("'", "'\\'\'")}'");
                }

                var argsConcat = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"";
                progress?.Report(0.96);
                RunProcess(ffmpegPath, argsConcat, _ => { progress?.Report(0.98); }, null);
                progress?.Report(1.0);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        private static bool IsInPath(string exeName)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var paths = path.Split(';');
                foreach (var p in paths)
                {
                    var candidate = Path.Combine(p, exeName);
                    if (File.Exists(candidate)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
