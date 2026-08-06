using System;
using System.IO;
using System.Text.Json;

namespace minhnhat_tool.Services
{
    /// <summary>Cài đặt CÀO NỀN (làm ấm cache hằng ngày) — lưu %AppData%\minhnhat_tool\caonen.json.</summary>
    public class CaoNenSettings
    {
        public bool Bat { get; set; } = false;          // bật/tắt cào nền
        public int GioChay { get; set; } = 2;           // giờ chạy trong ngày (0-23), mặc định 02:00
        public int SoNgay { get; set; } = 45;           // cửa sổ ngày cần làm ấm (đủ phủ kỳ khai thuế)
        public string LanChayCuoi { get; set; } = "";   // yyyy-MM-dd của lần chạy thành công gần nhất
        public int SoHoaDonLanCuoi { get; set; } = 0;   // số hóa đơn đã lưu ở lần cuối (để hiển thị)

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
    }
}
