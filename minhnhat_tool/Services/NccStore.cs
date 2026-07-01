using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace minhnhat_tool.Services
{
    /// <summary>Cache Nhà cung cấp HĐĐT theo MST ra %AppData%\minhnhat_tool\ncc_cache.json.
    /// Tra 1 lần (qua API tracuunnt có anti-bot) rồi nhớ luôn -> lần sau xuất Excel/dò tức thì.</summary>
    public static class NccStore
    {
        public class NccInfo { public string Ten { get; set; } = ""; public string Link { get; set; } = ""; }

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
        private static readonly string FilePath = Path.Combine(Dir, "ncc_cache.json");

        private static Dictionary<string, NccInfo>? _cache;

        private static Dictionary<string, NccInfo> Cache
        {
            get
            {
                if (_cache == null)
                {
                    try
                    {
                        _cache = File.Exists(FilePath)
                            ? JsonSerializer.Deserialize<Dictionary<string, NccInfo>>(File.ReadAllText(FilePath))
                              ?? new Dictionary<string, NccInfo>()
                            : new Dictionary<string, NccInfo>();
                    }
                    catch { _cache = new Dictionary<string, NccInfo>(); }
                }
                return _cache;
            }
        }

        public static bool TryGet(string mst, out NccInfo info)
        {
            if (!string.IsNullOrEmpty(mst) && Cache.TryGetValue(mst, out var v)) { info = v; return true; }
            info = new NccInfo();
            return false;
        }

        public static void Put(string mst, string ten, string link)
        {
            if (string.IsNullOrEmpty(mst)) return;
            Cache[mst] = new NccInfo { Ten = ten, Link = link };
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
