using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using VisionInspection.App.Imaging;
using VisionInspection.Camera.Offline;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Imaging;
using VisionInspection.Core.Models;
using VisionInspection.Vision.Teaching;

namespace VisionInspection.App.ViewModels
{
    /// <summary>
    /// 配方管理 VM：配方列表、逐工位标定（相机取图 / 样张 + 自动网格 + 可视化编辑）、示教、导入导出、切换。
    /// 支撑多产品换型——每个配方即一种产品，各工位 ROI 大小独立。
    /// </summary>
    public partial class RecipeManagementViewModel : ObservableObject
    {
        private readonly IRecipeStore _store;
        private readonly Func<int, int, ImageFrame> _captureFunc; // (行, 列) → 底图；演示相机据此生成，真机忽略参数
        private ImageFrame _referenceFrame; // 供自动识别的底图像素（与显示用 ReferenceImage 同源）

        [ObservableProperty] private ObservableCollection<string> _modelCodes = new ObservableCollection<string>();
        [ObservableProperty] private string _selectedModelCode;

        [ObservableProperty] private string _modelCode;
        [ObservableProperty] private string _recipeName;
        [ObservableProperty] private int _rows;
        [ObservableProperty] private int _columns;
        [ObservableProperty] private ObservableCollection<StationRowViewModel> _stations = new ObservableCollection<StationRowViewModel>();
        [ObservableProperty] private StationRowViewModel _selectedStation;
        [ObservableProperty] private string _statusMessage = "就绪";

        [ObservableProperty] private ImageSource _referenceImage;
        [ObservableProperty] private int _referenceImageWidth;
        [ObservableProperty] private int _referenceImageHeight;

        // 自动网格参数
        [ObservableProperty] private int _gridRows = 2;
        [ObservableProperty] private int _gridColumns = 3;
        [ObservableProperty] private int _gridPadding = 12;

        // 自动识别件极性（自动/亮件/暗件）
        [ObservableProperty] private PartPolarity _partPolarity = PartPolarity.Auto;
        public Array PartPolarityOptions => Enum.GetValues(typeof(PartPolarity));

        /// <summary>判定方法下拉仅列已实现的方法，避免选了未实现的方法而被静默回退到前景占比法。</summary>
        public DetectionMethod[] ImplementedMethods { get; } = { DetectionMethod.ForegroundRatio };

        public bool CanCaptureFromCamera => _captureFunc != null;

        public RecipeManagementViewModel(IRecipeStore store, Func<int, int, ImageFrame> captureFunc = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _captureFunc = captureFunc;
            RefreshList();
        }

        private void RefreshList() => ModelCodes = new ObservableCollection<string>(_store.ListModelCodes());

        partial void OnSelectedModelCodeChanged(string value)
        {
            if (!string.IsNullOrEmpty(value) && _store.TryLoad(value, out var r))
                LoadIntoEditor(r);
        }

        partial void OnSelectedStationChanged(StationRowViewModel value)
        {
            foreach (var s in Stations) s.IsSelected = ReferenceEquals(s, value);
        }

        private void LoadIntoEditor(Recipe r)
        {
            ModelCode = r.ModelCode;
            RecipeName = r.Name;
            Rows = r.Rows;
            Columns = r.Columns;
            Stations = new ObservableCollection<StationRowViewModel>(
                (r.Stations ?? new List<Station>()).Select(s => new StationRowViewModel(s)));
            StatusMessage = $"已加载配方 {r.ModelCode}（{r.StationCount} 工位）";
        }

        private Recipe BuildRecipe() => new Recipe
        {
            ModelCode = (ModelCode ?? string.Empty).Trim(),
            Name = RecipeName,
            Rows = Rows,
            Columns = Columns,
            Stations = Stations.Select(s => s.ToStation()).ToList(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        [RelayCommand]
        private void NewRecipe()
        {
            ModelCode = string.Empty;
            RecipeName = string.Empty;
            Rows = 0;
            Columns = 0;
            Stations = new ObservableCollection<StationRowViewModel>();
            StatusMessage = "新建配方（取图 → 生成网格或画框 → 示教 → 保存）";
        }

        // —— 标定底图 ——

        /// <summary>从相机取一帧作为标定底图（现场用真机 / 演示用模拟相机）。</summary>
        [RelayCommand]
        private void CaptureFromCamera()
        {
            if (_captureFunc == null) { StatusMessage = "未配置相机取图能力"; return; }
            try
            {
                var frame = _captureFunc(Rows, Columns); // 演示相机据当前配方行列生成底图；真机忽略参数
                _referenceFrame = frame;
                ReferenceImage = WpfImage.ToBitmapSource(frame);
                ReferenceImageWidth = frame.Width;
                ReferenceImageHeight = frame.Height;
                StatusMessage = $"已从相机取图 {frame.Width}×{frame.Height}";
            }
            catch (Exception ex)
            {
                StatusMessage = "取图失败：" + ex.Message;
            }
        }

        /// <summary>加载样张图片作为标定底图（对话框在 code-behind）。同时留存像素帧供自动识别。</summary>
        public void LoadReferenceImage(string path)
        {
            try
            {
                var frame = OfflineImageCamera.LoadAsFrame(path);
                _referenceFrame = frame;
                ReferenceImage = WpfImage.ToBitmapSource(frame);
                ReferenceImageWidth = frame.Width;
                ReferenceImageHeight = frame.Height;
                StatusMessage = $"已加载参考图 {frame.Width}×{frame.Height}";
            }
            catch (Exception ex)
            {
                StatusMessage = "加载样张失败：" + ex.Message;
            }
        }

        // —— 工位 ROI 编辑 ——

        /// <summary>按行列自动生成等距网格 ROI（规则阵列打底，之后可逐个拖拽微调）。</summary>
        [RelayCommand]
        private void GenerateGrid()
        {
            int rows = Math.Max(1, GridRows);
            int cols = Math.Max(1, GridColumns);
            int pad = Math.Max(0, GridPadding);
            int w = ReferenceImageWidth > 0 ? ReferenceImageWidth : 640;
            int h = ReferenceImageHeight > 0 ? ReferenceImageHeight : 480;
            int cellW = w / cols, cellH = h / rows;

            var list = new ObservableCollection<StationRowViewModel>();
            int idx = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    list.Add(new StationRowViewModel
                    {
                        Index = idx,
                        Row = r,
                        Column = c,
                        Name = $"工位{idx}",
                        X = c * cellW + pad,
                        Y = r * cellH + pad,
                        Width = Math.Max(1, cellW - 2 * pad),
                        Height = Math.Max(1, cellH - 2 * pad),
                        Threshold = 0.1,
                        Enabled = true
                    });
                    idx++;
                }

            Stations = list;
            Rows = rows;
            Columns = cols;
            StatusMessage = $"已生成 {rows}×{cols} 网格（可在图上逐个拖拽 / 缩放微调）";
        }

        /// <summary>基于底图自动识别板件位置，生成贴合各件的 ROI（替代等距网格的手动微调）。</summary>
        [RelayCommand]
        private void AutoDetectStations()
        {
            if (_referenceFrame == null)
            {
                StatusMessage = "自动识别失败：请先「相机取图」或「加载样张」";
                return;
            }
            try
            {
                var parts = new PartLocator().Locate(_referenceFrame,
                    new PartLocatorOptions { Polarity = PartPolarity });
                if (parts.Count == 0)
                {
                    StatusMessage = "未识别到工位：可切换 亮件/暗件 极性，或改用「生成网格」";
                    return;
                }

                var list = new ObservableCollection<StationRowViewModel>();
                int idx = 0;
                foreach (var p in parts)
                {
                    list.Add(new StationRowViewModel
                    {
                        Index = idx,
                        Row = p.Row,
                        Column = p.Column,
                        Name = $"工位{idx}",
                        X = p.Roi.X,
                        Y = p.Roi.Y,
                        Width = p.Roi.Width,
                        Height = p.Roi.Height,
                        Threshold = 0.1,
                        Enabled = true
                    });
                    idx++;
                }
                Stations = list;
                Rows = parts.Max(p => p.Row) + 1;     // 依识别结果回填行列数
                Columns = parts.Max(p => p.Column) + 1;
                StatusMessage = $"自动识别到 {parts.Count} 个工位（可在图上逐个拖拽 / 缩放微调）";
            }
            catch (Exception ex)
            {
                StatusMessage = "自动识别失败：" + ex.Message;
            }
        }

        [RelayCommand]
        private void AddStation()
        {
            Stations.Add(new StationRowViewModel
            {
                Index = Stations.Count,
                Name = $"工位{Stations.Count}",
                Threshold = 0.1,
                X = 20,
                Y = 20,
                Width = 80,
                Height = 80,
                Enabled = true
            });
        }

        [RelayCommand]
        private void RemoveStation()
        {
            if (SelectedStation != null) Stations.Remove(SelectedStation);
            Reindex();
        }

        /// <summary>在参考图上框选新增工位（图形标定，图像像素坐标）。</summary>
        public void AddStationAt(int x, int y, int width, int height)
        {
            var row = new StationRowViewModel
            {
                Index = Stations.Count,
                Name = $"工位{Stations.Count}",
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Threshold = 0.1,
                Enabled = true
            };
            Stations.Add(row);
            SelectedStation = row;
            StatusMessage = $"已添加工位 #{row.Index}（{x},{y} {width}×{height}）";
        }

        private void Reindex()
        {
            for (int i = 0; i < Stations.Count; i++) Stations[i].Index = i;
        }

        // —— 持久化 / 示教 ——

        [RelayCommand]
        private void Save()
        {
            if (string.IsNullOrWhiteSpace(ModelCode))
            {
                StatusMessage = "保存失败：型号码不能为空";
                return;
            }
            try
            {
                var recipe = BuildRecipe();
                _store.Save(recipe);
                RefreshList();
                SelectedModelCode = recipe.ModelCode;
                StatusMessage = $"已保存配方 {recipe.ModelCode}（{recipe.StationCount} 工位）";
            }
            catch (Exception ex)
            {
                StatusMessage = "保存失败：" + ex.Message;
            }
        }

        [RelayCommand]
        private void Delete()
        {
            if (string.IsNullOrEmpty(SelectedModelCode)) return;
            _store.Delete(SelectedModelCode);
            RefreshList();
            NewRecipe();
            StatusMessage = "已删除配方";
        }

        public void ExportTo(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(BuildRecipe(), Formatting.Indented));
            StatusMessage = "已导出：" + path;
        }

        public void ImportFrom(string path)
        {
            var r = JsonConvert.DeserializeObject<Recipe>(File.ReadAllText(path));
            if (r == null || string.IsNullOrWhiteSpace(r.ModelCode))
            {
                StatusMessage = "导入失败：文件内容无效";
                return;
            }
            _store.Save(r);
            RefreshList();
            SelectedModelCode = r.ModelCode;
            StatusMessage = "已导入配方 " + r.ModelCode;
        }

        public void TeachFromFiles(IEnumerable<string> presentFiles, IEnumerable<string> absentFiles)
        {
            var recipe = BuildRecipe();
            if (recipe.StationCount == 0)
            {
                StatusMessage = "示教失败：请先添加工位";
                return;
            }

            var present = LoadFrames(presentFiles);
            var absent = LoadFrames(absentFiles);
            if (present.Count == 0 || absent.Count == 0)
            {
                StatusMessage = "示教失败：满件与缺件样张各需至少 1 张";
                return;
            }

            var thresholds = new ThresholdTeacher().Teach(recipe, present, absent);
            foreach (var row in Stations)
                if (thresholds.TryGetValue(row.Index, out var th))
                    row.Threshold = Math.Round(th, 4);

            StatusMessage = $"示教完成：更新 {thresholds.Count} 个工位阈值（请记得保存）";
        }

        private static List<ImageFrame> LoadFrames(IEnumerable<string> files)
        {
            var list = new List<ImageFrame>();
            if (files == null) return list;
            foreach (var f in files)
                if (File.Exists(f))
                    list.Add(OfflineImageCamera.LoadAsFrame(f));
            return list;
        }
    }
}
