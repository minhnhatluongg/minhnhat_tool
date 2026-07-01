using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    /// <summary>Lưu danh sách doanh nghiệp ra file JSON trong %AppData%\minhnhat_tool.</summary>
    public static class DoanhNghiepStore
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
        private static readonly string FilePath = Path.Combine(Dir, "doanhnghiep.json");

        public static List<DoanhNghiep> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<DoanhNghiep>();
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<DoanhNghiep>>(json) ?? new List<DoanhNghiep>();
            }
            catch { return new List<DoanhNghiep>(); }
        }

        public static void Save(List<DoanhNghiep> list)
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
