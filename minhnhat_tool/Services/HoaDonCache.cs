using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using minhnhat_tool.Models;

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
        // Mặc định %AppData%\minhnhat_tool. Đặt biến môi trường MINHNHAT_CACHE_DIR để chuyển kho
        // sang ổ khác — cào cả năm kèm XML có thể lên vài GB, không phải máy nào cũng dư ổ C.
        private static readonly string Dir =
            Environment.GetEnvironmentVariable("MINHNHAT_CACHE_DIR") is string d && d.Trim().Length > 0
                ? d.Trim()
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
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
                CREATE TABLE IF NOT EXISTS xml(key TEXT PRIMARY KEY, data BLOB NOT NULL, at INTEGER);
                CREATE TABLE IF NOT EXISTS kho(key TEXT NOT NULL, mst TEXT NOT NULL, loai TEXT, ngay TEXT,
                                               doitac TEXT, tien REAL, pos INTEGER, at INTEGER,
                                               PRIMARY KEY(key, mst));
                CREATE INDEX IF NOT EXISTS ix_kho_mst ON kho(mst, loai, ngay);
                CREATE TABLE IF NOT EXISTS datra(hash TEXT PRIMARY KEY, at INTEGER);";
                cmd.ExecuteNonQuery();

                // Bản đầu của bảng kho khóa chính chỉ theo key -> hai công ty trên cùng máy (làm sổ
                // cho cả bên bán lẫn bên mua) đè mất nhau. Phát hiện schema cũ thì dựng lại; kho chỉ
                // là sổ tra cứu, lần đồng bộ/cào nền sau tự ghi lại đầy đủ.
                using (var kt = cn.CreateCommand())
                {
                    kt.CommandText = "SELECT COUNT(*) FROM pragma_table_info('kho') WHERE pk > 0";
                    if (Convert.ToInt32(kt.ExecuteScalar() ?? 0) != 2)
                    {
                        kt.CommandText = @"DROP TABLE IF EXISTS kho;
                            CREATE TABLE kho(key TEXT NOT NULL, mst TEXT NOT NULL, loai TEXT, ngay TEXT,
                                             doitac TEXT, tien REAL, pos INTEGER, at INTEGER,
                                             PRIMARY KEY(key, mst));
                            CREATE INDEX IF NOT EXISTS ix_kho_mst ON kho(mst, loai, ngay);";
                        kt.ExecuteNonQuery();
                    }
                }
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
                    cmd.CommandText = "DELETE FROM detail; DELETE FROM xml; DELETE FROM kho; VACUUM;";
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

        // ===== SỔ ĐÃ TRẢ TIỀN (hạn mức theo tờ) =====
        // Ghi lại tờ nào đã được máy chủ tính tiền, để: (1) khỏi hỏi lại máy chủ mỗi lần cào,
        // (2) mất mạng vẫn tải tiếp được phần đã mua. CỐ Ý không xóa cùng cache: đây là chứng từ
        // đã trả tiền, không phải dữ liệu tải lại được. Mất cũng không sao — máy chủ vẫn giữ sổ
        // gốc và sẽ trả về "miễn phí", chỉ tốn thêm một lượt gọi mạng.

        /// <summary>Những hash đã trả tiền trong danh sách đưa vào.</summary>
        public static HashSet<string> LocDaTra(IEnumerable<string> hashes)
        {
            var co = new HashSet<string>(StringComparer.Ordinal);
            var ds = hashes?.Where(h => !string.IsNullOrEmpty(h)).Distinct(StringComparer.Ordinal).ToList();
            if (ds == null || ds.Count == 0) return co;
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                const int LO = 400;
                for (int i = 0; i < ds.Count; i += LO)
                {
                    int n = Math.Min(LO, ds.Count - i);
                    using var cmd = cn.CreateCommand();
                    var ten = new string[n];
                    for (int j = 0; j < n; j++)
                    {
                        ten[j] = "$h" + j;
                        cmd.Parameters.AddWithValue(ten[j], ds[i + j]);
                    }
                    cmd.CommandText = "SELECT hash FROM datra WHERE hash IN (" + string.Join(",", ten) + ")";
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) co.Add(rd.GetString(0));
                }
            }
            catch { }
            return co;
        }

        /// <summary>Ghi nhận các tờ vừa được máy chủ cấp phép.</summary>
        public static void GhiDaTra(IEnumerable<string> hashes)
        {
            if (hashes == null) return;
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var tx = cn.BeginTransaction();
                    using var cmd = cn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR IGNORE INTO datra(hash,at) VALUES($h,$t)";
                    var pH = cmd.Parameters.Add("$h", SqliteType.Text);
                    var pT = cmd.Parameters.Add("$t", SqliteType.Integer);
                    pT.Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    foreach (var h in hashes)
                    {
                        if (string.IsNullOrEmpty(h)) continue;
                        pH.Value = h;
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
            catch { }
        }

        /// <summary>Số tờ đã trả tiền đang ghi nhận trên máy này.</summary>
        public static int DemDaTra()
        {
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                return Dem(cn, "datra");
            }
            catch { return 0; }
        }

        // ===== SỔ KHO: hóa đơn nào ĐÃ có trên máy, của công ty nào, chiều nào, tháng nào =====
        // Bảng detail/xml chỉ biết "có dữ liệu theo khóa", không biết thuộc về ai -> không thống kê
        // được. Bảng kho lưu phần định danh để trả lời "kho của tôi đang có gì".
        // Cố ý KHÔNG lưu cờ "đã có chi tiết/XML": lúc thống kê nối thẳng sang detail/xml nên số liệu
        // không bao giờ lệch với dữ liệu thật.

        /// <summary>Ghi nhận một hóa đơn vào sổ kho. mstChu = MST tài khoản đã tra ra hóa đơn này.</summary>
        public static void GhiKho(HoaDonInfo hd, string mstChu)
        {
            if (hd == null || string.IsNullOrWhiteSpace(mstChu)) return;
            GhiKho(new[] { hd }, mstChu);
        }

        /// <summary>Ghi nhận cả lô (dùng sau khi đồng bộ hoặc cào nền xong một chiều).</summary>
        public static void GhiKho(System.Collections.Generic.IEnumerable<HoaDonInfo> ds, string mstChu)
        {
            if (ds == null || string.IsNullOrWhiteSpace(mstChu)) return;
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var tx = cn.BeginTransaction();
                    using var cmd = cn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT OR REPLACE INTO kho(key,mst,loai,ngay,doitac,tien,pos,at)
                                        VALUES($k,$m,$l,$n,$dt,$ti,$p,$t)";
                    var pK = cmd.Parameters.Add("$k", SqliteType.Text);
                    var pM = cmd.Parameters.Add("$m", SqliteType.Text);
                    var pL = cmd.Parameters.Add("$l", SqliteType.Text);
                    var pN = cmd.Parameters.Add("$n", SqliteType.Text);
                    var pD = cmd.Parameters.Add("$dt", SqliteType.Text);
                    var pTi = cmd.Parameters.Add("$ti", SqliteType.Real);
                    var pP = cmd.Parameters.Add("$p", SqliteType.Integer);
                    var pT = cmd.Parameters.Add("$t", SqliteType.Integer);
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string chu = mstChu.Trim();
                    foreach (var hd in ds)
                    {
                        if (hd == null) continue;
                        string loai = string.Equals(hd.Nbmst?.Trim(), chu, StringComparison.OrdinalIgnoreCase) ? "ra" : "vao";
                        pK.Value = Key(hd.Nbmst ?? "", hd.Khhdon ?? "", hd.Shdon ?? "", hd.Khmshdon ?? "");
                        pM.Value = chu;
                        pL.Value = loai;
                        pN.Value = DateTime.TryParse(hd.Tdlap, out var d) ? d.ToString("yyyy-MM-dd") : "";
                        pD.Value = (loai == "ra" ? hd.Nmten : hd.Nbten) ?? "";
                        pTi.Value = (double)hd.Tgtttbso;
                        pP.Value = hd.MayTinhTien ? 1 : 0;
                        pT.Value = now;
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
            catch { }
        }

        /// <summary>Một dòng thống kê kho (theo công ty + chiều hóa đơn, hoặc theo tháng).</summary>
        public class DongKho
        {
            public string Mst { get; set; } = "";
            public string Ten { get; set; } = "";        // tên công ty, UI điền từ DoanhNghiepStore
            public string Loai { get; set; } = "";       // "Mua vào" / "Bán ra"
            public string Ky { get; set; } = "";         // tháng (khi thống kê theo tháng)
            public int SoHoaDon { get; set; }
            public int CoChiTiet { get; set; }
            public int CoXml { get; set; }
            public int Pos { get; set; }                 // số hóa đơn máy tính tiền
            public string TuNgay { get; set; } = "";
            public string DenNgay { get; set; } = "";
            public decimal TongTien { get; set; }

            public string LoaiRaw { get; set; } = "";    // "vao" / "ra" (để lọc, không hiển thị)
            public string DayDu => SoHoaDon == 0 ? "" : $"{CoChiTiet * 100.0 / SoHoaDon:F0}%";
            public string TongTienHienThi => TongTien.ToString("N0");
        }

        /// <summary>Thống kê kho theo từng công ty + chiều hóa đơn.</summary>
        public static System.Collections.Generic.List<DongKho> ThongKeTheoCty()
        {
            var kq = new System.Collections.Generic.List<DongKho>();
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
                    SELECT k.mst, k.loai, COUNT(*),
                           SUM(CASE WHEN d.key IS NOT NULL THEN 1 ELSE 0 END),
                           SUM(CASE WHEN x.key IS NOT NULL THEN 1 ELSE 0 END),
                           SUM(k.pos), MIN(NULLIF(k.ngay,'')), MAX(k.ngay), SUM(k.tien), ''
                    FROM kho k
                    LEFT JOIN detail d ON d.key = k.key
                    LEFT JOIN xml    x ON x.key = k.key
                    GROUP BY k.mst, k.loai
                    ORDER BY k.mst, k.loai DESC";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) kq.Add(DocDong(rd, coKy: false));
            }
            catch { }
            return kq;
        }

        /// <summary>Thống kê kho theo THÁNG cho một công ty + chiều (mst/loai rỗng = tất cả).</summary>
        public static System.Collections.Generic.List<DongKho> ThongKeTheoThang(string mst, string loai)
        {
            var kq = new System.Collections.Generic.List<DongKho>();
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
                    SELECT k.mst, k.loai, COUNT(*),
                           SUM(CASE WHEN d.key IS NOT NULL THEN 1 ELSE 0 END),
                           SUM(CASE WHEN x.key IS NOT NULL THEN 1 ELSE 0 END),
                           SUM(k.pos), MIN(NULLIF(k.ngay,'')), MAX(k.ngay), SUM(k.tien),
                           substr(k.ngay,1,7) AS thang
                    FROM kho k
                    LEFT JOIN detail d ON d.key = k.key
                    LEFT JOIN xml    x ON x.key = k.key
                    WHERE ($m = '' OR k.mst = $m) AND ($l = '' OR k.loai = $l) AND k.ngay <> ''
                    GROUP BY thang
                    ORDER BY thang DESC";
                cmd.Parameters.AddWithValue("$m", mst ?? "");
                cmd.Parameters.AddWithValue("$l", loai ?? "");
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) kq.Add(DocDong(rd, coKy: true));
            }
            catch { }
            return kq;
        }

        private static DongKho DocDong(SqliteDataReader rd, bool coKy)
        {
            string loai = rd.IsDBNull(1) ? "" : rd.GetString(1);
            var d = new DongKho
            {
                Mst = rd.IsDBNull(0) ? "" : rd.GetString(0),
                LoaiRaw = loai,
                Loai = loai == "ra" ? "Bán ra" : "Mua vào",
                SoHoaDon = rd.GetInt32(2),
                CoChiTiet = rd.IsDBNull(3) ? 0 : rd.GetInt32(3),
                CoXml = rd.IsDBNull(4) ? 0 : rd.GetInt32(4),
                Pos = rd.IsDBNull(5) ? 0 : rd.GetInt32(5),
                TuNgay = rd.IsDBNull(6) ? "" : NgayVn(rd.GetString(6)),
                DenNgay = rd.IsDBNull(7) ? "" : NgayVn(rd.GetString(7)),
                TongTien = rd.IsDBNull(8) ? 0m : (decimal)rd.GetDouble(8)
            };
            if (coKy && !rd.IsDBNull(9))
            {
                var t = rd.GetString(9);                       // yyyy-MM
                d.Ky = t.Length == 7 ? $"{t.Substring(5, 2)}/{t.Substring(0, 4)}" : t;
            }
            return d;
        }

        private static string NgayVn(string iso)
            => DateTime.TryParse(iso, out var d) ? d.ToString("dd/MM/yyyy") : iso;

        /// <summary>Tổng quan kho: (số hóa đơn, có chi tiết, có XML, ngày sớm nhất, ngày muộn nhất).</summary>
        public static (int soHd, int coCt, int coXml, string tu, string den) TongQuanKho()
        {
            try
            {
                BaoDamSanSang();
                using var cn = new SqliteConnection(ConnStr); cn.Open();
                using var cmd = cn.CreateCommand();
                // DISTINCT: một tờ hóa đơn xuất hiện ở cả bên bán lẫn bên mua (khi máy này làm sổ
                // cho cả hai công ty) vẫn chỉ là MỘT tờ trong kho.
                cmd.CommandText = @"
                    SELECT COUNT(DISTINCT k.key),
                           COUNT(DISTINCT d.key),
                           COUNT(DISTINCT x.key),
                           MIN(NULLIF(k.ngay,'')), MAX(k.ngay)
                    FROM kho k
                    LEFT JOIN detail d ON d.key = k.key
                    LEFT JOIN xml    x ON x.key = k.key";
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                    return (rd.GetInt32(0),
                            rd.IsDBNull(1) ? 0 : rd.GetInt32(1),
                            rd.IsDBNull(2) ? 0 : rd.GetInt32(2),
                            rd.IsDBNull(3) ? "" : NgayVn(rd.GetString(3)),
                            rd.IsDBNull(4) ? "" : NgayVn(rd.GetString(4)));
            }
            catch { }
            return (0, 0, 0, "", "");
        }

        /// <summary>Xóa dữ liệu kho của một công ty (kèm chi tiết/XML chỉ thuộc riêng công ty đó).</summary>
        public static int XoaTheoCty(string mst)
        {
            if (string.IsNullOrWhiteSpace(mst)) return 0;
            try
            {
                BaoDamSanSang();
                lock (_ghi)
                {
                    using var cn = new SqliteConnection(ConnStr); cn.Open();
                    using var cmd = cn.CreateCommand();
                    // Chỉ xóa detail/XML của hóa đơn KHÔNG còn công ty nào khác đang giữ trong kho,
                    // tránh làm mất dữ liệu dùng chung giữa nhiều công ty trên cùng máy.
                    cmd.CommandText = @"
                        CREATE TEMP TABLE IF NOT EXISTS _xoa(key TEXT PRIMARY KEY);
                        DELETE FROM _xoa;
                        INSERT INTO _xoa SELECT key FROM kho WHERE mst = $m
                          AND key NOT IN (SELECT key FROM kho WHERE mst <> $m);
                        DELETE FROM detail WHERE key IN (SELECT key FROM _xoa);
                        DELETE FROM xml    WHERE key IN (SELECT key FROM _xoa);
                        DELETE FROM kho    WHERE mst = $m;";
                    cmd.Parameters.AddWithValue("$m", mst.Trim());
                    return cmd.ExecuteNonQuery();
                }
            }
            catch { return 0; }
        }
    }
}
