using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace minhnhat_worker.Models
{
    public class HoaDonInfo
    {
        public string Khhdon { get; set; } = "";   // ký hiệu hóa đơn
        public string Shdon { get; set; } = "";     // số hóa đơn
        public string Tdlap { get; set; } = "";     // ngày lập
        public string Nbmst { get; set; } = "";     // MST người bán
        public string Nbten { get; set; } = "";     // tên người bán
        public decimal Tgtcthue { get; set; }        // tổng tiền chưa thuế
        public decimal Tgtthue { get; set; }         // tổng tiền thuế
        public decimal Tgtttbso { get; set; }        // tổng tiền thanh toán
    }
}
