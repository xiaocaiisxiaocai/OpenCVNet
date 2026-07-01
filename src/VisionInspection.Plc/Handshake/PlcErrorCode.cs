namespace VisionInspection.Plc.Handshake
{
    /// <summary>回写 PLC「错误码字」的数值定义。</summary>
    public static class PlcErrorCode
    {
        public const short None = 0;
        public const short NoRecipe = 1;     // 无匹配配方
        public const short AlignFail = 2;    // 定位配准失败
        public const short CameraError = 3;  // 相机 / 取像异常
        public const short Internal = 99;    // 内部异常

        /// <summary>由检测结果的字符串错误码映射到 PLC 数值错误码。</summary>
        public static short FromInspection(string code)
        {
            switch (code)
            {
                case "NO_RECIPE": return NoRecipe;
                case "ALIGN_FAIL": return AlignFail;
                case "CAMERA_ERROR": return CameraError;
                default: return Internal;
            }
        }
    }
}
