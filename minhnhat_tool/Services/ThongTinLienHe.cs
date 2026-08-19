namespace minhnhat_tool.Services
{
    /// <summary>Thông tin liên hệ mua khóa — hiển thị trong app khi khách chưa có khóa hoặc sắp hết
    /// hạn mức. SỬA Ở ĐÚNG ĐÂY, đừng rải trong giao diện.
    ///
    /// Để TRỐNG thì phần đó tự ẩn. Cố ý không đặt sẵn số điện thoại/email giả: thà không hiện gì
    /// còn hơn hiện một đầu mối không có thật rồi khách gọi vào chỗ trống.</summary>
    public static class ThongTinLienHe
    {
        public const string Zalo = "";        // ví dụ: "0901 234 567"
        public const string DienThoai = "";   // ví dụ: "1900 xxxx"
        public const string Email = "";       // ví dụ: "sales@wintech.vn"
        public const string Website = "";     // ví dụ: "https://wintvan.vn/bang-gia"

        public static bool CoThongTin =>
            Zalo.Length + DienThoai.Length + Email.Length + Website.Length > 0;

        /// <summary>Chuỗi một dòng để hiển thị, rỗng nếu chưa cấu hình gì.</summary>
        public static string MotDong()
        {
            var phan = new System.Collections.Generic.List<string>();
            if (DienThoai.Length > 0) phan.Add("Hotline " + DienThoai);
            if (Zalo.Length > 0) phan.Add("Zalo " + Zalo);
            if (Email.Length > 0) phan.Add(Email);
            if (Website.Length > 0) phan.Add(Website);
            return string.Join("  ·  ", phan);
        }
    }
}
