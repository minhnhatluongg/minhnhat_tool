namespace minhnhat_tool.Models
{
    public class HoaDonInfo
    {
        public string Khmshdon { get; set; } = "";  // ký hiệu mẫu số hóa đơn (cần để tra chi tiết)
        public string Khhdon { get; set; } = "";   // ký hiệu hóa đơn
        public string Shdon { get; set; } = "";     // số hóa đơn
        public string Tdlap { get; set; } = "";     // ngày lập
        public string Nbmst { get; set; } = "";     // MST người bán
        public string Nbten { get; set; } = "";     // tên người bán
        public string Nmmst { get; set; } = "";     // MST người mua
        public string Nmten { get; set; } = "";     // tên người mua
        public decimal Tgtcthue { get; set; }        // tổng tiền chưa thuế
        public decimal Tgtthue { get; set; }         // tổng tiền thuế
        public decimal Tgtttbso { get; set; }        // tổng tiền thanh toán
        public decimal Ttcktmai { get; set; }        // tổng chiết khấu thương mại
        public int Ttxly { get; set; }               // trạng thái xử lý (kết quả kiểm tra)
        public int Tthai { get; set; }               // trạng thái hóa đơn
        public string Nmdchi { get; set; } = "";     // địa chỉ người mua
        public string Dvtte { get; set; } = "VND";   // đơn vị tiền tệ
        public decimal Tgia { get; set; } = 1;       // tỷ giá
        public decimal Tgtphi { get; set; }          // tổng tiền phí
        public bool MayTinhTien { get; set; }         // true = HĐ có mã khởi tạo từ máy tính tiền (POS, từ /sco-query)
    }
}
