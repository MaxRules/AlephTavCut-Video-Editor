using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AlephTavCutVideoEditorApp;
using Xunit;

namespace AlephTavCutVideoEditorApp.Tests.Integration
{
    public class ExportIntegrationTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void ExportFlow_Reencode_PreciseTrim_Works_WhenFfmpegAvailable()
        {
            // locate ffmpeg
            var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            string ffmpegPath = null;
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) ffmpegPath = env;
            else
            {
                // look under tools folder used by the dev machine
                var candidates = Directory.GetFiles("C:", "ffmpeg.exe", SearchOption.AllDirectories);
                foreach (var c in candidates)
                {
                    if (c.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ffmpegPath = c; break;
                    }
                }
            }

            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                Console.WriteLine("ffmpeg not found on runner; skipping integration test.");
                return;
            }

            var tmp = Path.Combine(Path.GetTempPath(), "atce_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var input = Path.Combine(tmp, "input.mp4");
                // create a small synthetic 6s test video
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -f lavfi -i testsrc=duration=6:size=320x240:rate=30 -f lavfi -i sine=frequency=440:duration=6 -c:v libx264 -c:a aac -pix_fmt yuv420p \"{input}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(startInfo))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed to generate test input");
                }

                // define cuts to remove 2-3s
                var keep = new System.Collections.Generic.List<(TimeSpan Start, TimeSpan End)>
                {
                    (TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)),
                    (TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6))
                };

                var output = Path.Combine(tmp, "out.mp4");

                // run the app's export method with precise=true
                MainWindow.ExportUsingFfmpegWithProgress(input, keep, ffmpegPath, output, new Progress<double>(p => Console.WriteLine($"progress:{p}")), CancellationToken.None, true);

                Assert.True(File.Exists(output), "Output file not created");

                // read output duration
                var infoStart = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{output}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                string stderr;
                using (var p = Process.Start(infoStart))
                {
                    stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                }
                var line = Array.Find(stderr.Split('\n'), l => l.Contains("Duration"));
                Assert.False(string.IsNullOrEmpty(line), "Could not read output duration");
                var durStr = line.Split(',')[0].Replace("Duration:", "").Trim();
                var ts = TimeSpan.Parse(durStr);
                // expected duration approx 5 seconds (2 + 3)
                Assert.InRange(ts.TotalSeconds, 4.3, 5.7);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
