using System.Collections.Generic;
using VisionInspection.Core.Models;

namespace VisionInspection.Core.Abstractions
{
    /// <summary>
    /// 配方存储抽象。按型号码（ModelCode）持久化 / 加载配方，支撑多产品换型。
    /// </summary>
    public interface IRecipeStore
    {
        /// <summary>列出所有已存配方的型号码。</summary>
        IReadOnlyList<string> ListModelCodes();

        bool Exists(string modelCode);

        /// <summary>加载指定型号配方；不存在时抛 <see cref="KeyNotFoundException"/>。</summary>
        Recipe Load(string modelCode);

        /// <summary>尝试加载；不存在返回 false。</summary>
        bool TryLoad(string modelCode, out Recipe recipe);

        void Save(Recipe recipe);

        void Delete(string modelCode);
    }
}
