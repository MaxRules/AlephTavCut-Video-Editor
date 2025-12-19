using System;
using System.Collections.Generic;
using Xunit;

namespace AlephTavCutVideoEditorApp.Tests
{
    public class MainLogicTests
    {
        [Fact]
        public void ParseTimestamp_SecondsAndHms_Works()
        {
            var t1 = ParseTimestamp("90.5");
            Assert.Equal(TimeSpan.FromSeconds(90.5), t1);
            var t2 = ParseTimestamp("00:01:30");
            Assert.Equal(TimeSpan.FromSeconds(90), t2);
        }

        [Fact]
        public void ComputeKeepSegments_Works()
        {
            var duration = TimeSpan.FromSeconds(100);
            var cuts = new[] { (Start: TimeSpan.FromSeconds(10), End: TimeSpan.FromSeconds(20)), (Start: TimeSpan.FromSeconds(50), End: TimeSpan.FromSeconds(70)) };
            var keep = ComputeKeepSegments(duration, new List<(TimeSpan Start, TimeSpan End)>(cuts));
            Assert.Collection(keep,
                item => { Assert.Equal(TimeSpan.Zero, item.Start); Assert.Equal(TimeSpan.FromSeconds(10), item.End); },
                item => { Assert.Equal(TimeSpan.FromSeconds(20), item.Start); Assert.Equal(TimeSpan.FromSeconds(50), item.End); },
                item => { Assert.Equal(TimeSpan.FromSeconds(70), item.Start); Assert.Equal(TimeSpan.FromSeconds(100), item.End); }
            );
        }

        private static TimeSpan ParseTimestamp(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Empty timestamp");
            if (text.Contains(":"))
                return TimeSpan.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            var sec = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            return TimeSpan.FromSeconds(sec);
        }

        private static List<(TimeSpan Start, TimeSpan End)> ComputeKeepSegments(TimeSpan duration, List<(TimeSpan Start, TimeSpan End)> cuts)
        {
            var keep = new List<(TimeSpan Start, TimeSpan End)>();
            var last = TimeSpan.Zero;
            foreach (var c in cuts)
            {
                if (c.Start > last) keep.Add((last, c.Start));
                last = c.End;
            }
            if (last < duration) keep.Add((last, duration));
            return keep.FindAll(k => k.End > k.Start);
        }
    }
}
