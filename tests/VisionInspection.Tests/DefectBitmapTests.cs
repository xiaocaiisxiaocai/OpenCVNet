using System;
using System.Collections.Generic;
using VisionInspection.Core.Inspection;
using VisionInspection.Core.Models;
using Xunit;

namespace VisionInspection.Tests
{
    public class DefectBitmapTests
    {
        [Fact]
        public void RequiredWordCount_Rounds_Up()
        {
            Assert.Equal(0, DefectBitmap.RequiredWordCount(0));
            Assert.Equal(1, DefectBitmap.RequiredWordCount(1));
            Assert.Equal(1, DefectBitmap.RequiredWordCount(16));
            Assert.Equal(2, DefectBitmap.RequiredWordCount(17));
        }

        [Fact]
        public void Encode_Sets_Bits_For_Absent_Stations()
        {
            var results = new List<StationResult>
            {
                new StationResult(0, PresenceState.Present, 0.9, 0.5),
                new StationResult(1, PresenceState.Absent, 0.1, 0.5),
                new StationResult(16, PresenceState.Absent, 0.05, 0.5),
            };

            var words = DefectBitmap.Encode(results, wordCount: 2, unknownAsDefect: true);

            Assert.Equal(2, words.Length);
            Assert.Equal((ushort)0b0000_0000_0000_0010, words[0]); // 工位1 缺件
            Assert.Equal((ushort)0b0000_0000_0000_0001, words[1]); // 工位16 缺件（第2个字的 bit0）
            Assert.Equal(new List<int> { 1, 16 }, DefectBitmap.GetDefectIndices(words));
        }

        [Fact]
        public void Encode_Unknown_As_Defect_Is_Configurable()
        {
            var results = new List<StationResult> { new StationResult(2, PresenceState.Unknown, 0, 0.5) };

            var on = DefectBitmap.Encode(results, 1, unknownAsDefect: true);
            Assert.Equal((ushort)0b100, on[0]);

            var off = DefectBitmap.Encode(results, 1, unknownAsDefect: false);
            Assert.Equal((ushort)0, off[0]);
        }

        [Fact]
        public void Encode_Throws_When_Index_Exceeds_Reserved_Words()
        {
            var results = new List<StationResult> { new StationResult(16, PresenceState.Absent, 0, 0.5) };
            Assert.Throws<ArgumentException>(() => DefectBitmap.Encode(results, wordCount: 1));
        }
    }
}
