using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Models;

namespace VisionInspection.Infrastructure.Storage
{
    /// <summary>
    /// 基于 JSON 文件的配方存储：每个配方存为 &lt;根目录&gt;\&lt;型号码&gt;.json。
    /// 型号码用作文件名，非法字符会被拒绝以防路径穿越；写入采用“临时文件 + 替换”保证原子性。
    /// </summary>
    public sealed class JsonRecipeStore : IRecipeStore
    {
        private readonly string _rootDir;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        public JsonRecipeStore(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir))
                throw new ArgumentException("配方根目录不能为空。", nameof(rootDir));
            _rootDir = rootDir;
            Directory.CreateDirectory(_rootDir);
        }

        public IReadOnlyList<string> ListModelCodes()
        {
            if (!Directory.Exists(_rootDir)) return new List<string>();
            return Directory.GetFiles(_rootDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool Exists(string modelCode) => File.Exists(PathOf(modelCode));

        public Recipe Load(string modelCode)
        {
            var path = PathOf(modelCode);
            if (!File.Exists(path))
                throw new KeyNotFoundException($"配方不存在：{modelCode}");

            var recipe = JsonConvert.DeserializeObject<Recipe>(File.ReadAllText(path), Settings);
            if (recipe == null)
                throw new InvalidDataException($"配方文件内容无效：{path}");
            return recipe;
        }

        public bool TryLoad(string modelCode, out Recipe recipe)
        {
            recipe = null;
            var path = PathOf(modelCode);
            if (!File.Exists(path)) return false;
            recipe = JsonConvert.DeserializeObject<Recipe>(File.ReadAllText(path), Settings);
            return recipe != null;
        }

        public void Save(Recipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (string.IsNullOrWhiteSpace(recipe.ModelCode))
                throw new ArgumentException("配方型号码不能为空。", nameof(recipe));

            var path = PathOf(recipe.ModelCode);
            var json = JsonConvert.SerializeObject(recipe, Settings);

            // 原子写：先写临时文件，再替换正式文件，避免写入中断导致配方损坏。
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        public void Delete(string modelCode)
        {
            var path = PathOf(modelCode);
            if (File.Exists(path)) File.Delete(path);
        }

        private string PathOf(string modelCode)
        {
            if (string.IsNullOrWhiteSpace(modelCode))
                throw new ArgumentException("型号码不能为空。", nameof(modelCode));
            if (modelCode.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"型号码含非法字符：{modelCode}", nameof(modelCode));
            return Path.Combine(_rootDir, modelCode + ".json");
        }
    }
}
