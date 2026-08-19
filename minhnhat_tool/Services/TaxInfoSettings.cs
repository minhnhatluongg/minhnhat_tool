using System;
using System.IO;
using System.Text.Json;

namespace minhnhat_tool.Services
{
    /// <summary>Cấu hình dịch vụ tra cứu tình trạng người nộp thuế (taxinfo).
    ///
    /// KHÓA API KHÔNG ĐƯỢC HARDCODE trong mã nguồn — kho mã này công khai, hardcode là lộ khóa.
    /// Lưu tại %AppData%\minhnhat_tool\taxinfo.json (ngoài kho mã), nhập một lần trong Cài đặt.</summary>
    public class TaxInfoSettings
    {
        public string BaseUrl { get; set; } = "https://taxinfo-new.wintvan.vn";
        public string ApiKey { get; set; } = "";
        public string ApiSecret { get; set; } = "";

        /// <summary>Tự kiểm tra nhà cung cấp ngay sau khi đồng bộ hóa đơn xong.</summary>
        public bool TuKiemTraSauDongBo { get; set; } = true;

        public bool DaCauHinh => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "minhnhat_tool");
        private static readonly string FilePath = Path.Combine(Dir, "taxinfo.json");
        private static TaxInfoSettings? _hienTai;

        public static TaxInfoSettings HienTai
        {
            get
            {
                if (_hienTai == null)
                {
                    try
                    {
                        _hienTai = File.Exists(FilePath)
                            ? JsonSerializer.Deserialize<TaxInfoSettings>(File.ReadAllText(FilePath)) ?? new TaxInfoSettings()
                            : new TaxInfoSettings();
                    }
                    catch { _hienTai = new TaxInfoSettings(); }
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
    }
}
