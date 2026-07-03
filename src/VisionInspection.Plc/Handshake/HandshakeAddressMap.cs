namespace VisionInspection.Plc.Handshake
{
    /// <summary>
    /// 握手软元件地址映射（默认值为示例，现场按 PLC 程序实际地址调整）。
    /// </summary>
    public sealed class HandshakeAddressMap
    {
        public string TriggerBit { get; set; } = "M100";        // PLC→PC 触发检测
        public string BusyBit { get; set; } = "M110";           // PC→PLC 正在检测
        public string DoneBit { get; set; } = "M111";           // PC→PLC 检测完成
        public string OkBit { get; set; } = "M112";             // PC→PLC 总判定 OK
        public string NgBit { get; set; } = "M113";             // PC→PLC 总判定 NG
        public string HeartbeatBit { get; set; } = "M120";      // PC→PLC 心跳

        public string ModelCodeWord { get; set; } = "D190";     // PLC→PC 当前型号码
        public bool UseSequence { get; set; } = false;           // 可选：PLC→PC 周期序号确认模式
        public string RequestSequenceWord { get; set; } = "D191";// PLC→PC 本次触发序号
        public string AckSequenceWord { get; set; } = "D211";    // PC→PLC 已完成序号
        public string DefectBitmapWord { get; set; } = "D200";  // PC→PLC 缺件位图起始
        public int DefectBitmapWordCount { get; set; } = 8;     // 预留字数（8 字 = 最多 128 工位）
        public string ErrorCodeWord { get; set; } = "D210";     // PC→PLC 错误码
    }
}
