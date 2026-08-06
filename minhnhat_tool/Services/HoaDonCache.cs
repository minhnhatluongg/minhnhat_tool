using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace minhnhat_tool.Services
{
    /// <summary>Cache BỀN dữ liệu BẤT BIẾN của hóa đơn (chi tiết JSON + XML gốc) ra SQLite tại
    /// %AppData%\minhnhat_tool\cache.db. Hóa đơn không đổi sau khi đã ký -> cache vĩnh viễn, giúp
    /// xuất Excel/PDF/bảng kê lần sau khỏi tải lại từ TCT.
    ///
    /// NGUYÊN TẮC ĐỘ CHÍNH XÁC: chỉ lưu thứ đã chắc chắn đúng. "Bị chặn/timeout" KHÔNG được đưa vào đây.
    /// Khóa = nbmst|khhdon|shdon|khmshdon (định danh hóa đơn, duy nhất toàn cục).
    ///
    /// An toàn đa luồng: bật WAL (đọc song song được) + khóa _ghi quanh mọi lệnh ghi.</summary>
    public static class HoaDonCache
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
        private static readonly string FilePath = Path.Combine(Dir, "cache.db");
        private static readonly string ConnStr = $"Data Source={FilePath}";
        private static readonly object _ghi = new();
        private static bool _san;

        public static string Key(string nbmst, string khhdon, string shdon, string khmshdon)
            => $"{nbmst}|{khhdon}|{shdon}|{khmshdon}";

        private static void BaoDamSanSang()
        {
            if (_san) return;
            lock (_ghi)
            {
                if (_san) return;
                Directory.CreateDirectory(Dir);
                using var cn = new SqliteConnection(ConnStr);
                cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS detail(key TEXT PRIMARY KEY, json TEXT NOT NULL, at INTEGER);
CREATE TABLE IF NOT EXISTS xml(key TEXT PRIMARY KEY, data BLOB NOT NULL, at INTEGER);";
                cmd.ExecuteNonQuery();
                _san = true;
            }
        }

        // ===== CHI TIẾT (JSON). json="" nghĩa là TCT khẳng định KHÔNG có chi tiết -> vẫn cache. =====
        public static bool TryDetail(string key, out string json)
        {
            json = "";
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT json FROM detail WHERE key=$k";
                cmd.Parameters.AddWithValue("$k", key);
                using var rd = cmd.ExecuteReader();
                if (rd.Read()) { json = rd.IsDBNull(0) ? "" : rd.GetString(0); return true; }
            }
            catch { }
            return false;
        }

        public static void PutDetail(string key, string json)
        {
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "INSERT OR REPLACE INTO detail(key,json,at) VALUES($k,$j,$t)";
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.Parameters.AddWithValue("$j", json ?? "");
                    cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        // ===== XML gốc (BLOB) =====
        public static bool TryXml(string key, out byte[] data)
        {
            data = Array.Empty<byte>();
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT data FROM xml WHERE key=$k";
                cmd.Parameters.AddWithValue("$k", key);
                using var rd = cmd.ExecuteReader();
                if (rd.Read() && !rd.IsDBNull(0)) { data = (byte[])rd["data"]; return data.Length > 0; }
            }
            catch { }
            return false;
        }

        public static void PutXml(string key, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "INSERT OR REPLACE INTO xml(key,data,at) VALUES($k,$d,$t)";
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.Parameters.AddWithValue("$d", data);
                    cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        // ===== Quản lý =====
        /// <summary>Xóa toàn bộ cache (tải lại từ TCT lần sau).</summary>
        public static void Xoa()
        {
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "DELETE FROM detail; DELETE FROM xml; VACUUM;";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        /// <summary>(số hóa đơn có chi tiết, số XML, dung lượng file MB) — để hiển thị.</summary>
        public static (int soDetail, int soXml, double mb) ThongKe()
        {
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                int d = Dem(cn, "detail"), x = Dem(cn, "xml");
                double mb = 0;
                try { mb = new FileInfo(FilePath).Length / 1024.0 / 1024.0; } catch { }
                return (d, x, mb);
            }
            catch { return (0, 0, 0); }
        }

        private static int Dem(SqliteConnection cn, string bang)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {bang}";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
    }
}
