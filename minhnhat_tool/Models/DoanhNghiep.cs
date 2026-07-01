namespace minhnhat_tool.Models
{
    public class DoanhNghiep
    {
        public string Mst { get; set; } = "";
        public string TenDN { get; set; } = "";
        public string Password { get; set; } = "";   // mật khẩu hoadondientu

        // Hiển thị trong ListBox
        public override string ToString() => string.IsNullOrEmpty(TenDN) ? Mst : $"{Mst} - {TenDN}";
    }
}
