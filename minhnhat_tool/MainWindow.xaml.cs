using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using minhnhat_tool.Models;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    public partial class MainWindow : Window
    {
        // Danh sách hóa đơn hiển thị trên bảng (ObservableCollection tự cập nhật UI)
        private readonly ObservableCollection<InvoiceRow> _hoaDon = new();
        private readonly HoaDonDienTuClient _tct = new();
        private string _loaiHD = "purchase";   // purchase = Mua vào (đầu vào), sold = Bán ra (đầu ra)
        private string _token = "";            // token đăng nhập gần nhất (để Xem/Tải XML)
        private string _lastLoai = "purchase";
        private bool _lastIsMuaVao = true;

        public MainWindow()
        {
            InitializeComponent();
            grdHoaDon.ItemsSource = _hoaDon;
            // Mặc định: từ đầu tháng này -> hôm nay
            dpTuNgay.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dpDenNgay.SelectedDate = DateTime.Today;
            UpdateDnButton();
            // Kiểm tra cập nhật ngầm khi mở app (chỉ chạy khi đã cài qua Setup)
            Loaded += async (_, __) => await Services.UpdateService.CheckAsync();
        }

        // Đổ danh sách hóa đơn thật lên bảng + tính tổng
        private void FillGrid(List<HoaDonInfo> list, bool isMuaVao)
        {
            _hoaDon.Clear();
            decimal sChua = 0, sThue = 0, sTong = 0;
            foreach (var hd in list)
            {
                _hoaDon.Add(new InvoiceRow
                {
                    TrangThaiXml = "Đã tải",
                    KyHieu = hd.Khhdon,
                    SoHoaDon = hd.Shdon,
                    NgayLap = FormatNgay(hd.Tdlap),
                    NguoiBan = isMuaVao ? hd.Nbten : Session.TenDN,
                    NguoiMua = isMuaVao ? Session.TenDN : hd.Nmten,
                    TongChuaThue = hd.Tgtcthue.ToString("N0"),
                    TongThue = hd.Tgtthue.ToString("N0"),
                    TongThanhToan = hd.Tgtttbso.ToString("N0"),
                    TrangThai = "Hóa đơn mới",
                    Raw = hd
                });
                sChua += hd.Tgtcthue;
                sThue += hd.Tgtthue;
                sTong += hd.Tgtttbso;
            }
            lblChuaThue.Text = $"Chưa thuế: {sChua:N0} VNĐ";
            lblThue.Text = $"Thuế: {sThue:N0} VNĐ";
            lblTong.Text = $"Tổng thanh toán: {sTong:N0} VNĐ";
            lblSoLuong.Text = $"Số lượng XML: {list.Count}/{list.Count}";
        }

        private static string FormatNgay(string raw)
            => System.DateTime.TryParse(raw, out var d) ? d.ToString("dd/MM/yyyy") : raw;

        // Mở cửa sổ Quản lý doanh nghiệp; chọn xong -> cập nhật nút góc trên
        private void btnChonDN_Click(object sender, RoutedEventArgs e)
        {
            var w = new DoanhNghiepWindow { Owner = this };
            if (w.ShowDialog() == true)
                UpdateDnButton();
        }

        private void UpdateDnButton()
        {
            btnChonDN.Content = string.IsNullOrEmpty(Session.Mst)
                ? "📁  Chưa chọn doanh nghiệp"
                : $"🏢  {Session.TenDN}  ({Session.Mst})";
        }

        // Chuyển Mua vào (đầu vào) <-> Bán ra (đầu ra)
        private void btnLoaiHD_Click(object sender, RoutedEventArgs e)
        {
            _loaiHD = (sender == btnBanRa) ? "sold" : "purchase";
            UpdateLoaiHDButtons();
        }

        private void UpdateLoaiHDButtons()
        {
            var green = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#16a34a")!;
            var gray = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1f2937")!;
            btnMuaVao.Background = _loaiHD == "purchase" ? green : gray;
            btnBanRa.Background = _loaiHD == "sold" ? green : gray;
        }

        private void btnTimNoiBo_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("TODO: tìm trong dữ liệu đã tải.");

        // ===== Kỳ kế toán -> tự set Từ ngày / Đến ngày =====
        private void cboKy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dpTuNgay == null || dpDenNgay == null) return; // đang khởi tạo UI
            var basis = dpTuNgay.SelectedDate ?? DateTime.Today;
            switch (cboKy.SelectedIndex)
            {
                case 0: // Theo Ngày
                    dpDenNgay.SelectedDate = dpTuNgay.SelectedDate ?? DateTime.Today;
                    dpDenNgay.IsEnabled = false;
                    break;
                case 1: // Theo Tháng
                    var f = new DateTime(basis.Year, basis.Month, 1);
                    dpTuNgay.SelectedDate = f;
                    dpDenNgay.SelectedDate = f.AddMonths(1).AddDays(-1);
                    dpDenNgay.IsEnabled = false;
                    break;
                case 2: // Theo Quý
                    int q = (basis.Month - 1) / 3;
                    var qf = new DateTime(basis.Year, q * 3 + 1, 1);
                    dpTuNgay.SelectedDate = qf;
                    dpDenNgay.SelectedDate = qf.AddMonths(3).AddDays(-1);
                    dpDenNgay.IsEnabled = false;
                    break;
                case 3: // Theo Năm
                    dpTuNgay.SelectedDate = new DateTime(basis.Year, 1, 1);
                    dpDenNgay.SelectedDate = new DateTime(basis.Year, 12, 31);
                    dpDenNgay.IsEnabled = false;
                    break;
                default: // Tùy Chọn -> tự chọn tự do
                    dpDenNgay.IsEnabled = true;
                    break;
            }
        }

        // Chuột phải -> chọn dòng đó luôn (để context menu tác động đúng hóa đơn)
        private void Row_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Phải chuột trong vùng đang bôi đen -> giữ nguyên đa chọn.
            // Phải chuột ngoài vùng chọn -> chọn đúng dòng đó.
            if (sender is DataGridRow row && !row.IsSelected)
            {
                grdHoaDon.SelectedItems.Clear();
                row.IsSelected = true;
            }
        }

        // ===== Chuột phải trên dòng hóa đơn =====
        private async void mnuXem_Click(object sender, RoutedEventArgs e)
        {
            if (grdHoaDon.SelectedItem is not InvoiceRow row || row.Raw == null)
            { MessageBox.Show("Chọn 1 hóa đơn trong bảng."); return; }
            try
            {
                string detail = "";
                if (!string.IsNullOrEmpty(_token))
                    detail = await _tct.GetInvoiceDetailAsync(_token, row.Raw);
                HoaDonViewer.ShowInvoice(row.Raw, Session.TenDN, _lastIsMuaVao, detail);
            }
            catch (Exception ex) { MessageBox.Show("Lỗi xem hóa đơn: " + ex.Message); }
        }

        private async void mnuTaiXml_Click(object sender, RoutedEventArgs e)
        {
            if (grdHoaDon.SelectedItem is not InvoiceRow row || row.Raw == null)
            { MessageBox.Show("Chọn 1 hóa đơn trong bảng."); return; }
            if (string.IsNullOrEmpty(_token))
            { MessageBox.Show("Bấm 'Đồng bộ' trước để đăng nhập rồi mới tải được."); return; }
            try
            {
                var bytes = await _tct.ExportXmlAsync(_token, row.Raw);
                bool isZip = bytes.Length > 1 && bytes[0] == 0x50 && bytes[1] == 0x4B; // "PK" = zip
                string ext = isZip ? "zip" : "xml";
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = isZip ? "Zip (*.zip)|*.zip" : "XML (*.xml)|*.xml",
                    FileName = $"HD_{row.Raw.Khhdon}_{row.Raw.Shdon}.{ext}"
                };
                if (dlg.ShowDialog() == true)
                {
                    System.IO.File.WriteAllBytes(dlg.FileName, bytes);
                    MessageBox.Show("Đã tải XML gốc:\n" + dlg.FileName);
                }
            }
            catch (Exception ex) { MessageBox.Show("Lỗi tải XML: " + ex.Message); }
        }

        // hoadondientu KHÔNG có file PDF trực tiếp -> mở bản Xem (giống 100%) để Ctrl+P lưu PDF
        private void mnuTaiPdf_Click(object sender, RoutedEventArgs e)
        {
            if (grdHoaDon.SelectedItem is not InvoiceRow row || row.Raw == null)
            { MessageBox.Show("Chọn 1 hóa đơn trong bảng."); return; }
            MessageBox.Show("Hệ thống Tổng cục Thuế không cung cấp file PDF trực tiếp (chỉ có XML).\n\n" +
                            "Tôi mở bản XEM HÓA ĐƠN (giống 100% bản thuế) — trong trình duyệt bấm Ctrl+P → chọn 'Lưu dạng PDF' để có file PDF.",
                            "Lưu PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            mnuXem_Click(sender, e);
        }

        // Xuất TẤT CẢ hóa đơn đang hiển thị
        private async void mnuExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_hoaDon.Count == 0) { MessageBox.Show("Chưa có hóa đơn để xuất."); return; }
            await ExportExcelAsync(_hoaDon.ToList());
        }

        // Xuất các dòng ĐANG BÔI ĐEN (giữ Ctrl/Shift để chọn nhiều) — nhanh hơn khi chỉ cần vài tờ
        private async void mnuExcelChon_Click(object sender, RoutedEventArgs e)
        {
            var sel = grdHoaDon.SelectedItems.Cast<InvoiceRow>().ToList();
            if (sel.Count == 0)
            { MessageBox.Show("Bôi đen các dòng cần xuất trước.\nGiữ Ctrl để chọn rời, giữ Shift để chọn 1 dải."); return; }
            await ExportExcelAsync(sel);
        }

        // Xuất Excel đầy đủ: sheet TongHop + sheet ChiTiet (dòng hàng) + Mã tra cứu
        private async Task ExportExcelAsync(List<InvoiceRow> rows)
        {
            if (rows.Count == 0) { MessageBox.Show("Chưa có hóa đơn để xuất."); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"HoaDon_{Session.Mst}_{rows.Count}to_{DateTime.Now:yyyyMMdd_HHmm}.xlsx" };
            if (dlg.ShowDialog() != true) return;

            // Luôn tải chi tiết (nếu đã đăng nhập) để có Mã tra cứu + dòng hàng — bản đầy đủ
            bool canDetail = !string.IsNullOrEmpty(_token);

            ShowProgress($"Đang xuất Excel {rows.Count} hóa đơn...");
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var wsT = wb.AddWorksheet("TongHop");
                string[] hT = { "STT","Ký hiệu mẫu số","Ký hiệu hóa đơn","Số hóa đơn","Ngày lập",
                                "MST người bán/xuất hàng","Tên người bán/xuất hàng","MST người mua/nhận hàng","Tên người mua/nhận hàng",
                                "Địa chỉ người mua","Tổng tiền chưa thuế","Tổng tiền thuế","Tổng tiền chiết khấu thương mại",
                                "Tổng tiền phí","Tổng tiền thanh toán","Đơn vị tiền tệ","Tỷ giá","Trạng thái hóa đơn","Kết quả kiểm tra hóa đơn",
                                "Nhà cung cấp HĐĐT","Link tra cứu","Thông tin tra cứu",
                                "Hóa đơn liên quan","Thông tin liên quan" };

                // Tiêu đề giống file thuế
                string tuN = dpTuNgay.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
                string denN = dpDenNgay.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
                wsT.Cell(1, 1).Value = "DANH SÁCH HÓA ĐƠN";
                var tr1 = wsT.Range(1, 1, 1, hT.Length); tr1.Merge();
                tr1.Style.Font.Bold = true; tr1.Style.Font.FontSize = 14;
                tr1.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                wsT.Cell(2, 1).Value = $"Từ ngày {tuN} đến ngày {denN}";
                var tr2 = wsT.Range(2, 1, 2, hT.Length); tr2.Merge();
                tr2.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                int headRowT = 3;
                for (int c = 0; c < hT.Length; c++) wsT.Cell(headRowT, c + 1).Value = hT[c];

                var wsC = wb.AddWorksheet("ChiTiet");
                string[] hC = { "Ký hiệu","Số HĐ","STT","Tên hàng hóa, dịch vụ","ĐVT","Số lượng","Đơn giá (chưa thuế)",
                                "Tỷ lệ CK (%)","Tiền chiết khấu","Thành tiền chưa thuế","Thuế suất","Tiền thuế","Thành tiền sau thuế" };
                for (int c = 0; c < hC.Length; c++) wsC.Cell(1, c + 1).Value = hC[c];

                int rT = headRowT + 1, rC = 2, i = 0;
                foreach (var row in rows)
                {
                    i++;
                    var hd = row.Raw!;
                    string nccCol = "", linkCol = "", maCol = "", hdLienQuanCol = "", ttLienQuanCol = "";

                    if (canDetail)
                    {
                        SetProgress(i, rows.Count, $"Đang xuất Excel {i}/{rows.Count} hóa đơn...");
                        // Retry vì TCT hay chặn khi gọi chi tiết liên tục
                        string dj = "";
                        for (int a = 0; a < 3 && string.IsNullOrEmpty(dj); a++)
                        {
                            if (a > 0) await Task.Delay(500 * a);
                            try { dj = await _tct.GetInvoiceDetailAsync(_token, hd); } catch { dj = ""; }
                        }
                        try
                        {
                            if (!string.IsNullOrEmpty(dj))
                            {
                                var r = JsonDocument.Parse(dj).RootElement;
                                // Nhà cung cấp HĐĐT + link tra cứu + mã tra cứu
                                if (r.TryGetProperty("cttkhac", out var ctArr) && ctArr.ValueKind == JsonValueKind.Array)
                                    foreach (var cti in ctArr.EnumerateArray())
                                        if (cti.TryGetProperty("ttruong", out var ctFld) && (ctFld.GetString() ?? "").StartsWith("Mã tra cứu"))
                                            maCol = ExS(cti, "dlieu");
                                var ncc = await ResolveNccAsync(ExS(r, "msttcgp"), hd.Nbmst, ExS(r, "id"), maCol);
                                nccCol = ncc.ten; linkCol = ncc.link;
                                if (r.TryGetProperty("hdhhdvu", out var arr) && arr.ValueKind == JsonValueKind.Array)
                                    foreach (var it in arr.EnumerateArray())
                                    {
                                        double thtien = ExD(it, "thtien");        // thành tiền chưa thuế
                                        double tsuat  = ExD(it, "tsuat");          // thuế suất dạng số (0.08)
                                        double tthue  = ExD(it, "tthue");          // tiền thuế/dòng (hay null)
                                        if (tthue <= 0) tthue = Math.Round(thtien * tsuat, 0);  // null -> tự tính
                                        wsC.Cell(rC, 1).Value  = hd.Khhdon;
                                        wsC.Cell(rC, 2).Value  = hd.Shdon;
                                        wsC.Cell(rC, 3).Value  = ExS(it, "stt");
                                        wsC.Cell(rC, 4).Value  = ExS(it, "ten");
                                        wsC.Cell(rC, 5).Value  = ExS(it, "dvtinh");
                                        wsC.Cell(rC, 6).Value  = ExD(it, "sluong");
                                        wsC.Cell(rC, 7).Value  = ExD(it, "dgia");        // đơn giá chưa thuế
                                        wsC.Cell(rC, 8).Value  = ExD(it, "tlckhau");     // tỷ lệ chiết khấu %
                                        wsC.Cell(rC, 9).Value  = ExD(it, "stckhau");     // tiền chiết khấu
                                        wsC.Cell(rC, 10).Value = thtien;                 // thành tiền chưa thuế
                                        wsC.Cell(rC, 11).Value = ExS(it, "ltsuat");      // thuế suất "8%"
                                        wsC.Cell(rC, 12).Value = tthue;                  // tiền thuế
                                        wsC.Cell(rC, 13).Value = thtien + tthue;         // thành tiền sau thuế
                                        rC++;
                                    }
                            }
                        }
                        catch { }

                        // Hóa đơn liên quan + Thông tin liên quan (thường rỗng, chỉ có khi HĐ bị điều chỉnh/thay thế/sai sót)
                        try { hdLienQuanCol = TomTatLienQuan(await _tct.GetRelativeAsync(_token, hd)); } catch { }
                        try { ttLienQuanCol = TomTatLienQuan(await _tct.GetRelatedAsync(_token, hd)); } catch { }

                        await Task.Delay(200);   // giãn cách để không bị TCT chặn
                    }

                    wsT.Cell(rT, 1).Value = i;
                    wsT.Cell(rT, 2).Value = hd.Khmshdon;
                    wsT.Cell(rT, 3).Value = hd.Khhdon;
                    wsT.Cell(rT, 4).Value = hd.Shdon;
                    wsT.Cell(rT, 5).Value = row.NgayLap;
                    wsT.Cell(rT, 6).Value = hd.Nbmst;
                    wsT.Cell(rT, 7).Value = hd.Nbten;
                    wsT.Cell(rT, 8).Value = hd.Nmmst;
                    wsT.Cell(rT, 9).Value = hd.Nmten;
                    wsT.Cell(rT, 10).Value = hd.Nmdchi;
                    wsT.Cell(rT, 11).Value = hd.Tgtcthue;
                    wsT.Cell(rT, 12).Value = hd.Tgtthue;
                    wsT.Cell(rT, 13).Value = hd.Ttcktmai;
                    wsT.Cell(rT, 14).Value = hd.Tgtphi;
                    wsT.Cell(rT, 15).Value = hd.Tgtttbso;
                    wsT.Cell(rT, 16).Value = hd.Dvtte;
                    wsT.Cell(rT, 17).Value = hd.Tgia;
                    wsT.Cell(rT, 18).Value = TrangThaiHD(hd.Tthai);
                    wsT.Cell(rT, 19).Value = KetQua(hd.Ttxly);
                    wsT.Cell(rT, 20).Value = nccCol;
                    wsT.Cell(rT, 21).Value = linkCol;
                    wsT.Cell(rT, 22).Value = maCol;
                    wsT.Cell(rT, 23).Value = hdLienQuanCol;
                    wsT.Cell(rT, 24).Value = ttLienQuanCol;
                    rT++;
                }

                StyleSheet(wsT, headRowT, hT.Length);
                StyleSheet(wsC, 1, hC.Length);
                wb.SaveAs(dlg.FileName);
                MessageBox.Show($"Đã xuất Excel {rows.Count} hóa đơn:\n{dlg.FileName}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Lỗi xuất Excel: " + ex.Message); }
            finally { HideProgress(); }
        }

        private static string ExS(JsonElement e, string n)
            => e.TryGetProperty(n, out var v)
               ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ValueKind == JsonValueKind.Number ? v.ToString() : "")
               : "";
        private static double ExD(JsonElement e, string n)
            => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        // Tóm tắt hóa đơn/thông tin liên quan -> 1 chuỗi ngắn cho ô Excel (thường rỗng)
        private static string TomTatLienQuan(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement arr;
                if (root.ValueKind == JsonValueKind.Array) arr = root;
                else if (root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("datas", out var d) && d.ValueKind == JsonValueKind.Array) arr = d;
                else return "";

                var items = new List<string>();
                foreach (var el in arr.EnumerateArray())
                {
                    string kh = ExS(el, "khhdon"), sh = ExS(el, "shdon");
                    string s = string.IsNullOrEmpty(kh) ? sh : (string.IsNullOrEmpty(sh) ? kh : $"{kh}-{sh}");
                    if (!string.IsNullOrEmpty(s)) items.Add(s);
                }
                if (items.Count > 0) return string.Join("; ", items);
                int n = arr.GetArrayLength();
                return n > 0 ? $"{n} mục" : "";
            }
            catch { return ""; }
        }

        // Định dạng sheet giống file thuế: header vàng, viền, freeze dòng tiêu đề
        private static void StyleSheet(ClosedXML.Excel.IXLWorksheet ws, int headerRow, int cols)
        {
            var head = ws.Range(headerRow, 1, headerRow, cols);
            head.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FFD200");
            head.Style.Font.Bold = true;
            head.Style.Alignment.WrapText = true;
            head.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            var used = ws.RangeUsed();
            if (used != null)
            {
                used.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                used.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
        }

        // Map trạng thái xử lý (ttxly) -> "Kết quả kiểm tra" (chỉnh lại nếu lệch với TCT)
        private static string KetQua(int t) => t switch
        {
            1 => "Tổng cục thuế đã tiếp nhận hóa đơn",
            2 => "Đã cấp mã hóa đơn",
            3 => "Hóa đơn không đủ điều kiện cấp mã",
            5 => "Đã cấp mã hóa đơn",
            6 => "Tổng cục thuế đã nhận hóa đơn không mã",
            7 => "Tổng cục thuế đã nhận hóa đơn không mã có sai sót",
            8 => "Tổng cục thuế đã nhận hóa đơn có mã khởi tạo từ máy tính tiền",
            _ => t == 0 ? "" : $"Mã trạng thái {t}"
        };

        private static string TrangThaiHD(int t) => t switch
        {
            1 => "Hóa đơn mới",
            2 => "Hóa đơn thay thế",
            3 => "Hóa đơn điều chỉnh",
            4 => "Hóa đơn bị thay thế",
            5 => "Hóa đơn bị điều chỉnh",
            6 => "Hóa đơn hủy",
            _ => t == 0 ? "" : $"Trạng thái {t}"
        };

        // Bảng ánh xạ Nhà cung cấp HĐĐT (TVAN) theo msttcgp -> tên + template link tra cứu.
        // Placeholder: {id}=GUID hóa đơn, {nbmst}=MST người bán, {ma}=mã tra cứu.
        private static readonly System.Collections.Generic.Dictionary<string, (string ten, string url)> _tvanMap = new()
        {
            ["0101360697"] = ("Công ty Cổ phần BKAV", "https://van.ehoadon.vn/Lookup?InvoiceGUID={id}"),
            ["0101243150"] = ("Công ty Cổ phần MISA", "https://www.meinvoice.vn/tra-cuu/"),
            ["0100684378"] = ("Tập đoàn Bưu chính Viễn thông VN (VNPT)", "https://{nbmst}-tt78.vnpt-invoice.com.vn"),
            ["0100109106"] = ("Tập đoàn CN - Viễn thông Quân đội (Viettel)", "https://vinvoice.viettel.vn/utilities/invoice-search"),
            ["0106026495"] = ("Công ty TNHH HĐĐT M-Invoice", "https://tracuuhoadon.minvoice.com.vn/tra-cuu"),
            ["0105987432"] = ("Công ty CP Đầu tư công nghệ EasyInvoice", "http://tracuu.easyinvoice.com.vn"),
            ["0106713804"] = ("Công ty Cổ phần Hilo Việt Nam", "https://tracuuhddt78.hilo.com.vn/"),
            ["0312303803"] = ("Công ty TNHH Win Tech Solution", "https://chungtu.wininvoice.vn/ct_ktt"),
        };

        /// <summary>Tìm Nhà cung cấp HĐĐT theo thứ tự: (1) bảng TVAN cứng -> tức thì,
        /// (2) cache file NccStore -> tức thì, (3) API tracuunnt (có anti-bot, chậm) -> lưu cache.</summary>
        private async System.Threading.Tasks.Task<(string ten, string link)> ResolveNccAsync(string msttcgp, string nbmst, string id, string ma)
        {
            if (string.IsNullOrEmpty(msttcgp)) return ("", "");

            // (1) Bảng TVAN cứng (có template link tra cứu)
            if (_tvanMap.TryGetValue(msttcgp, out var t))
            {
                string link = t.url.Replace("{id}", id).Replace("{nbmst}", nbmst).Replace("{ma}", ma);
                return ($"{msttcgp} - {t.ten}", link);
            }

            // (2) Cache đã tra trước đó -> lấy tức thì, không gọi API
            if (Services.NccStore.TryGet(msttcgp, out var cached))
                return ($"{msttcgp} - {cached.Ten}", cached.Link);

            // (3) Chưa biết -> gọi API tracuunnt (chậm vì anti-bot) rồi nhớ luôn
            var (ten, _) = await _tct.TcnntLookupAsync(msttcgp);
            if (!string.IsNullOrEmpty(ten))
            {
                Services.NccStore.Put(msttcgp, ten, "");   // link rỗng: NCC ngoài bảng chưa có template tra cứu
                return ($"{msttcgp} - {ten}", "");
            }

            return (msttcgp, "");   // vẫn không ra -> chỉ hiện MST
        }

        // ===== Progress overlay =====
        private void ShowProgress(string text, bool indeterminate = false)
        {
            txtProgress.Text = text;
            barProgress.IsIndeterminate = indeterminate;
            barProgress.Value = 0;
            barProgress.Maximum = 1;
            overlayProgress.Visibility = Visibility.Visible;
        }
        private void SetProgress(int cur, int total, string text)
        {
            barProgress.IsIndeterminate = false;
            barProgress.Maximum = total <= 0 ? 1 : total;
            barProgress.Value = cur;
            txtProgress.Text = text;
        }
        private void HideProgress() => overlayProgress.Visibility = Visibility.Collapsed;

        // Nút Đồng bộ: đăng nhập + cào hóa đơn TRỰC TIẾP rồi đổ lên bảng (in-process, Cách A)
        // (RabbitMQ/JobPublisher vẫn giữ trong Services — dùng lại khi làm cào tập trung nhiều công ty)
        private async void btnDongBo_Click(object sender, RoutedEventArgs e)
        {
            // Bắt buộc chọn doanh nghiệp trước
            if (string.IsNullOrEmpty(Session.Mst))
            {
                MessageBox.Show("Vui lòng chọn doanh nghiệp trước (bấm nút ở góc trên bên trái).",
                                "Chưa chọn doanh nghiệp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Nhắc user chọn thời gian
            if (dpTuNgay.SelectedDate == null || dpDenNgay.SelectedDate == null)
            {
                MessageBox.Show("Vui lòng chọn TỪ NGÀY và ĐẾN NGÀY!",
                                "Chưa chọn thời gian", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (dpTuNgay.SelectedDate > dpDenNgay.SelectedDate)
            {
                MessageBox.Show("TỪ NGÀY phải nhỏ hơn hoặc bằng ĐẾN NGÀY!",
                                "Thời gian không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            btnDongBo.IsEnabled = false;
            btnDongBo.Content = "⏳ Đang tải...";
            ShowProgress("Đang đăng nhập & tải hóa đơn từ Tổng cục Thuế...", indeterminate: true);
            try
            {
                _token = await _tct.LoginAsync(Session.Mst, Session.Password);
                _lastIsMuaVao = _loaiHD == "purchase";
                _lastLoai = _loaiHD;

                // Chia khoảng ngày thành từng tháng (TCT giới hạn <= 1 tháng/lần) -> hỗ trợ cả Quý/Năm
                var all = new List<HoaDonInfo>();
                var chunkStart = dpTuNgay.SelectedDate.Value;
                var to = dpDenNgay.SelectedDate.Value;
                while (chunkStart <= to)
                {
                    var chunkEnd = chunkStart.AddMonths(1).AddDays(-1);
                    if (chunkEnd > to) chunkEnd = to;
                    var part = await _tct.QueryInvoicesAsync(_token, _loaiHD,
                                   chunkStart.ToString("dd/MM/yyyy"), chunkEnd.ToString("dd/MM/yyyy"));
                    all.AddRange(part);
                    txtProgress.Text = $"Đang tải hóa đơn... (đã có {all.Count})";
                    chunkStart = chunkEnd.AddDays(1);
                }

                FillGrid(all, _lastIsMuaVao);
                MessageBox.Show($"Đã tải {all.Count} hóa đơn {(_lastIsMuaVao ? "MUA VÀO" : "BÁN RA")} của {Session.TenDN}.",
                                "Đồng bộ xong", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không tải được hóa đơn: " + ex.Message,
                                "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDongBo.IsEnabled = true;
                btnDongBo.Content = "☁  Đồng bộ Tổng cục Thuế";
                HideProgress();
            }
        }
    }
}
