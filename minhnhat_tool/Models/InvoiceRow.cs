using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace minhnhat_tool.Models
{
    public class InvoiceRow
    {
        public string TrangThaiXml { get; set; } = "";
        public string KyHieu { get; set; } = "";
        public string SoHoaDon { get; set; } = "";
        public string NgayLap { get; set; } = "";
        public string NguoiBan { get; set; } = "";
        public string NguoiMua { get; set; } = "";
        public string TongChuaThue { get; set; } = "";
        public string TongThue { get; set; } = "";
        public string TongThanhToan { get; set; } = "";
        public string TrangThai { get; set; } = "";

        // Dữ liệu gốc để Xem hóa đơn / Tải XML (không hiển thị trên bảng)
        public HoaDonInfo? Raw { get; set; }
    }
}
