using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace minhnhat_tool.Services
{
    /// <summary>Cài đặt CÀO NỀN (tải sẵn hóa đơn về máy) — lưu %AppData%\minhnhat_tool\caonen.json.
    /// Nói rõ CÀO GÌ (mua vào / bán ra) trong KHOẢNG NÀO, vì đó là hai thứ quyết định
    /// mất bao lâu và tốn bao nhiêu lượt gọi Tổng cục Thuế.</summary>
    public class CaoNenSettings
    {
        public bool Bat { get; set; } = false;              // bật/tắt cào nền tự động
        public int GioChay { get; set; } = 2;               // giờ chạy trong ngày (0-23)

        // ===== CÀO GÌ =====
        public bool CaoMuaVao { get; set; } = true;         // hóa đơn đầu vào (mua vào)
        public bool CaoBanRa { get; set; } = false;         // hóa đơn đầu ra (bán ra)
        public bool LayXml { get; set; } = true;            // tải kèm XML gốc (bản có giá trị pháp lý)

        // ===== KHOẢNG NÀO =====
        // "GanDay" = N ngày gần nhất (cuốn chiếu mỗi ngày) | "CoDinh" = đúng một khoảng ngày
        public string PhamVi { get; set; } = "GanDay";
        public int SoNgay { get; set; } = 45;               // dùng khi PhamVi = GanDay
        public string TuNgay { get; set; } = "";            // yyyy-MM-dd, dùng khi PhamVi = CoDinh
        public string DenNgay { get; set; } = "";

        // ===== KẾT QUẢ LẦN CHẠY GẦN NHẤT =====
        public string LanChayCuoi { get; set; } = "";       // yyyy-MM-dd
        public int SoHoaDonLanCuoi { get; set; } = 0;
        public string TomTatLanCuoi { get; set; } = "";     // câu mô tả để hiển thị lại sau khi mở app

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
        private static readonly string FilePath = Path.Combine(Dir, "caonen.json");
        private static CaoNenSettings? _hienTai;

        public static CaoNenSettings HienTai
        {
            get
            {
                if (_hienTai == null)
                {
                    try
                    {
                        _hienTai = File.Exists(FilePath)
                            ? JsonSerializer.Deserialize<CaoNenSettings>(File.ReadAllText(FilePath)) ?? new CaoNenSettings()
                            : new CaoNenSettings();
                    }
                    catch { _hienTai = new CaoNenSettings(); }
                }
                return _hienTai;
            }
        }

        public void Luu()
        {
            _hienTai = this;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public bool DaChayHomNay => LanChayCuoi == DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>Các chiều hóa đơn sẽ cào — rỗng nghĩa là người dùng chưa chọn gì (không chạy).</summary>
        public string[] CacLoai()
        {
            if (CaoMuaVao && CaoBanRa) return new[] { "purchase", "sold" };
            if (CaoBanRa) return new[] { "sold" };
            if (CaoMuaVao) return new[] { "purchase" };
            return Array.Empty<string>();
        }

        public string MoTaLoai() =>
            (CaoMuaVao && CaoBanRa) ? "mua vào + bán ra" : CaoBanRa ? "bán ra" : CaoMuaVao ? "mua vào" : "(chưa chọn)";

        /// <summary>Khoảng ngày người dùng YÊU CẦU (chưa áp giới hạn gói).</summary>
        public (DateTime tu, DateTime den) KhoangYeuCau()
        {
            if (PhamVi == "CoDinh")
            {
                var tu = ParseNgay(TuNgay) ?? DateTime.Today.AddDays(-45);
                var den = ParseNgay(DenNgay) ?? DateTime.Today;
                if (den < tu) (tu, den) = (den, tu);
                if (den > DateTime.Today) den = DateTime.Today;
                return (tu, den);
            }
            var d = DateTime.Today;
            return (d.AddDays(-Math.Max(1, SoNgay)), d);
        }

        public static DateTime? ParseNgay(string s)
            => DateTime.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var d) ? d : null;
    }
}
