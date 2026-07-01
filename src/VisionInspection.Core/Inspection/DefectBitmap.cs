using System;
using System.Collections.Generic;
using VisionInspection.Core.Models;

namespace VisionInspection.Core.Inspection
{
    /// <summary>
    /// 缺件位图编解码：把逐工位结果编码为供 PLC D 区回写的 ushort[]（每 bit 对应一个工位）。
    /// <para>约定：bit = 1 表示该工位“缺件 / 异常”，bit = 0 表示“有件”。</para>
    /// <para>工位由 <see cref="StationResult.StationIndex"/> 定位：word = Index / 16，bit = Index % 16（低位在前）。</para>
    /// <para>D 区长度按配方最大工位数预留，避免换型越界。</para>
    /// </summary>
    public static class DefectBitmap
    {
        /// <summary>根据工位数计算需要的字（16 位）个数。</summary>
        public static int RequiredWordCount(int stationCount)
        {
            if (stationCount < 0) throw new ArgumentOutOfRangeException(nameof(stationCount));
            return (stationCount + 15) / 16;
        }

        /// <summary>
        /// 编码为 ushort[]。<paramref name="wordCount"/> 为预留字数（PLC D 区长度）。
        /// <paramref name="unknownAsDefect"/> = true 时 Unknown 也置 1（安全侧，触发复核）。
        /// </summary>
        public static ushort[] Encode(IEnumerable<StationResult> stations, int wordCount, bool unknownAsDefect = true)
        {
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (wordCount < 0) throw new ArgumentOutOfRangeException(nameof(wordCount));

            var words = new ushort[wordCount];
            foreach (var s in stations)
            {
                bool defect = s.State == PresenceState.Absent
                              || (unknownAsDefect && s.State == PresenceState.Unknown);
                if (!defect) continue;

                if (s.StationIndex < 0)
                    throw new ArgumentException("工位序号不能为负：" + s.StationIndex);

                int word = s.StationIndex / 16;
                int bit = s.StationIndex % 16;
                if (word >= wordCount)
                    throw new ArgumentException(
                        $"工位序号 {s.StationIndex} 超出 D 区预留范围（wordCount = {wordCount}）。");

                words[word] |= (ushort)(1 << bit);
            }
            return words;
        }

        /// <summary>从位图取出所有“缺件”工位序号（用于测试 / 显示）。</summary>
        public static List<int> GetDefectIndices(ushort[] words)
        {
            if (words == null) throw new ArgumentNullException(nameof(words));

            var list = new List<int>();
            for (int w = 0; w < words.Length; w++)
                for (int b = 0; b < 16; b++)
                    if ((words[w] & (1 << b)) != 0)
                        list.Add(w * 16 + b);
            return list;
        }
    }
}
