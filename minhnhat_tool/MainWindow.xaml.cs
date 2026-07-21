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

        // Toàn bộ hóa đơn đã tải (nguồn để lọc nội bộ, không mất khi tìm kiếm)
        private readonly List<InvoiceRow> _hoaDonAll = new();

        // Đổ danh sách hóa đơn thật vào nguồn + hiển thị (qua bộ lọc)
        private void FillGrid(List<HoaDonInfo> list, bool isMuaVao)
        {
            _hoaDonAll.Clear();
            foreach (var hd in list)
            {
                _hoaDonAll.Add(new InvoiceRow
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
                    TrangThai = TrangThaiHD(hd.Tthai),
                    LoaiHD = hd.MayTinhTien ? "Máy tính tiền" : "Điện tử",
                    Raw = hd
                });
            }
            ApplyFilter();
        }

        // Lọc nội bộ theo ô tìm kiếm (MST người bán/mua, số HĐ, ký hiệu, tên) — chạy local, không gọi API
        private void ApplyFilter()
        {
            if (lblSoLuong == null) return;   // UI đang khởi tạo, các label chưa sẵn sàng
            string kw = (txtTimKiem?.Text ?? "").Trim().ToLowerInvariant();
            string tt = (cboTrangThai?.SelectedIndex ?? 0) > 0
                        ? ((ComboBoxItem)cboTrangThai.SelectedItem).Content?.ToString() ?? "" : "";
            int loaiIdx = cboLoaiCT?.SelectedIndex ?? 0;   // 0=Tất cả, 1=Điện tử, 2=Máy tính tiền
            grdHoaDon.ItemsSource = null;                  // tháo binding -> đổ hàng trăm dòng nhanh (tránh render O(n^2))
            _hoaDon.Clear();
            decimal sChua = 0, sThue = 0, sTong = 0; int soPos = 0;
            foreach (var r in _hoaDonAll)
            {
                if (kw.Length > 0 && !RowMatches(r, kw)) continue;
                if (tt.Length > 0 && r.TrangThai != tt) continue;
                bool pos = r.Raw?.MayTinhTien ?? false;
                if (loaiIdx == 1 && pos) continue;    // chỉ hóa đơn điện tử
                if (loaiIdx == 2 && !pos) continue;   // chỉ HĐ máy tính tiền
                _hoaDon.Add(r);
                if (pos) soPos++;
                if (r.Raw != null) { sChua += r.Raw.Tgtcthue; sThue += r.Raw.Tgtthue; sTong += r.Raw.Tgtttbso; }
            }
            grdHoaDon.ItemsSource = _hoaDon;               // gắn lại -> render 1 lần
            lblChuaThue.Text = $"Chưa thuế: {sChua:N0} VNĐ";
            lblThue.Text = $"Thuế: {sThue:N0} VNĐ";
            lblTong.Text = $"Tổng thanh toán: {sTong:N0} VNĐ";
            lblSoLuong.Text = $"Số lượng: {_hoaDon.Count}/{_hoaDonAll.Count}  (máy tính tiền: {soPos})";
        }

        private static bool RowMatches(InvoiceRow r, string kw)
        {
            var hd = r.Raw;
            return (r.SoHoaDon ?? "").ToLowerInvariant().Contains(kw)
                || (r.KyHieu ?? "").ToLowerInvariant().Contains(kw)
                || (r.NguoiBan ?? "").ToLowerInvariant().Contains(kw)
                || (r.NguoiMua ?? "").ToLowerInvariant().Contains(kw)
                || (hd?.Nbmst ?? "").ToLowerInvariant().Contains(kw)
                || (hd?.Nmmst ?? "").ToLowerInvariant().Contains(kw);
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

        // Mở Dashboard thống kê trên dữ liệu đang hiển thị
        private void btnThongKe_Click(object sender, RoutedEventArgs e)
        {
            var rows = _hoaDon.ToList();
            if (rows.Count == 0) { MessageBox.Show("Chưa có dữ liệu. Bấm 'Đồng bộ' để tải hóa đơn trước."); return; }
            new DashboardWindow(rows, _lastIsMuaVao) { Owner = this }.ShowDialog();
        }

        // Cache tình trạng NCC theo MST trong phiên (status động nên không lưu file lâu dài)
        private readonly Dictionary<string, (string status, bool risk)> _nccRisk = new();

        // 🛡 Kiểm tra rủi ro: tra tình trạng MST đối tác qua tracuunnt — CHẠY SONG SONG 4 luồng, chỉ tra MST duy nhất.
        private async void btnRuiRo_Click(object sender, RoutedEventArgs e)
        {
            var rows = _hoaDon.ToList();
            if (rows.Count == 0) { MessageBox.Show("Chưa có hóa đơn để kiểm tra."); return; }

            // Danh sách MST đối tác DUY NHẤT chưa có trong cache (nhiều HĐ cùng NCC -> tra 1 lần)
            var mstList = rows
                .Select(r => _lastIsMuaVao ? (r.Raw?.Nbmst ?? "") : (r.Raw?.Nmmst ?? ""))
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .Where(m => !_nccRisk.ContainsKey(m))
                .ToList();

            ShowProgress($"Đang kiểm tra {mstList.Count} nhà cung cấp (song song 4 luồng)...");
            int done = 0;
            using var sem = new System.Threading.SemaphoreSlim(4);   // tối đa 4 "worker" cùng lúc
            try
            {
                var tasks = mstList.Select(async mst =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        if (Cancelled) return;
                        var (_, _, status) = await _tct.TcnntLookupAsync(mst, Ct);
                        bool risk = !string.IsNullOrEmpty(status)
                                    && status.IndexOf("đang hoạt động", StringComparison.OrdinalIgnoreCase) < 0;
                        // Code sau await chạy trên UI thread -> ghi cache an toàn, không cần khóa
                        _nccRisk[mst] = (string.IsNullOrEmpty(status) ? "Không tra được" : status, risk);
                    }
                    finally
                    {
                        done++;
                        SetProgress(done, mstList.Count, $"Đã kiểm tra {done}/{mstList.Count} nhà cung cấp...");
                        sem.Release();
                    }
                }).ToList();
                await Task.WhenAll(tasks);

                // Map kết quả (từ cache) vào từng dòng hóa đơn
                int risky = 0;
                foreach (var r in rows)
                {
                    string mst = _lastIsMuaVao ? (r.Raw?.Nbmst ?? "") : (r.Raw?.Nmmst ?? "");
                    if (!string.IsNullOrEmpty(mst) && _nccRisk.TryGetValue(mst, out var info))
                    {
                        r.TinhTrangNcc = info.status; r.NccRuiRo = info.risk;
                        if (info.risk) risky++;
                    }
                }
                grdHoaDon.Items.Refresh();
                string prefix = Cancelled ? "Đã HỦY (kết quả 1 phần đã có). " : "Đã kiểm tra xong. ";
                MessageBox.Show(risky == 0
                    ? prefix + "Không phát hiện đối tác rủi ro."
                    : prefix + $"⚠ Phát hiện {risky} hóa đơn có đối tác RỦI RO (không ở trạng thái 'đang hoạt động').\nCác dòng đã được tô đỏ.",
                    "Kết quả kiểm tra rủi ro");
            }
            catch (Exception ex) { MessageBox.Show("Lỗi kiểm tra rủi ro: " + ex.Message); }
            finally { HideProgress(); }
        }

        // Tìm nội bộ: lọc trên dữ liệu ĐÃ tải (không gọi lại TCT). Gõ tới đâu lọc tới đó.
        private void btnTimNoiBo_Click(object sender, RoutedEventArgs e) => ApplyFilter();
        private void txtTimKiem_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void cboTrangThai_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
        private void cboLoaiCT_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

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

            // 2 cột "liên quan" tốn THÊM 2 lượt gọi TCT cho MỖI hóa đơn mà hầu như luôn rỗng -> để người dùng chọn
            bool layLienQuan = false;
            if (canDetail)
                layLienQuan = MessageBox.Show(
                    "Có lấy thêm 2 cột 'Hóa đơn liên quan' và 'Thông tin liên quan' không?\n\n" +
                    "• Chọn KHÔNG (khuyên dùng): nhanh hơn nhiều. Hai cột này hầu như luôn rỗng,\n" +
                    "  chỉ có dữ liệu khi hóa đơn bị điều chỉnh / thay thế / sai sót.\n" +
                    "• Chọn CÓ: đầy đủ nhưng phải gọi thêm 2 lượt cho mỗi hóa đơn.",
                    "Xuất nhanh hay đầy đủ?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            ShowProgress($"Đang xuất Excel {rows.Count} hóa đơn...");
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var wsT = wb.AddWorksheet("TongHop");
                string[] hT = { "STT","Ký hiệu mẫu số","Ký hiệu hóa đơn","Số hóa đơn","Ngày lập","Loại HĐ",
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

                // ===== GIAI DOAN 1: tai CHI TIET SONG SONG =====
                // Truoc day chay TUAN TU tung to (moi to vai luot goi + delay) nen rat cham.
                var djs = new string[rows.Count];
                var lqs = new string[rows.Count];
                var ttlqs = new string[rows.Count];
                for (int k = 0; k < rows.Count; k++) { djs[k] = ""; lqs[k] = ""; ttlqs[k] = ""; }

                if (canDetail)
                {
                    // Dùng CHUNG bộ tải chi tiết ưu tiên độ chính xác (2 luồng + quét lại tuần tự)
                    djs = await LayChiTietDayDuAsync(rows);

                    // 2 cột "liên quan" (tùy chọn) — chạy riêng, cũng đi nhẹ để khỏi bị chặn
                    if (layLienQuan && !Cancelled)
                    {
                        int done = 0;
                        using var sem = new System.Threading.SemaphoreSlim(2);
                        var tasks = new List<Task>();
                        for (int k = 0; k < rows.Count; k++)
                        {
                            int idx = k;
                            tasks.Add(Task.Run(async () =>
                            {
                                await sem.WaitAsync(Ct);
                                try
                                {
                                    var hd2 = rows[idx].Raw!;
                                    try { lqs[idx] = TomTatLienQuan(await _tct.GetRelativeAsync(_token, hd2, Ct)); }
                                    catch (OperationCanceledException) { throw; } catch { }
                                    try { ttlqs[idx] = TomTatLienQuan(await _tct.GetRelatedAsync(_token, hd2, Ct)); }
                                    catch (OperationCanceledException) { throw; } catch { }
                                }
                                finally
                                {
                                    sem.Release();
                                    int n = System.Threading.Interlocked.Increment(ref done);
                                    Dispatcher.Invoke(() => SetProgress(n, rows.Count, $"Đang lấy thông tin liên quan {n}/{rows.Count}..."));
                                }
                            }, Ct));
                        }
                        try { await Task.WhenAll(tasks); }
                        catch (OperationCanceledException) { }   // Huy -> van xuat phan da tai duoc
                    }
                }

                // ===== GIAI DOAN 2: tra Nha cung cap HDDT - MOI MST DUNG 1 LAN =====
                // Nut that cu: tra TRUOT khong duoc nho, nen moi hoa don lai goi lai tracuunnt
                // (max_tries=12&delay=1.5 -> toi ~18 giay/lan) => 37 hoa don mat ~15 phut.
                var canTra = new HashSet<string>();
                foreach (var d in djs)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    try
                    {
                        var rr = JsonDocument.Parse(d).RootElement;
                        string m = ExS(rr, "msttcgp");
                        if (m.Length > 0 && !_tvanMap.ContainsKey(m) && !_nccTenCache.ContainsKey(m)
                            && !Services.NccStore.TryGet(m, out _))
                            canTra.Add(m);
                    }
                    catch { }
                }
                if (canTra.Count > 0 && !Cancelled)
                {
                    int dn = 0;
                    SetProgress(0, canTra.Count, $"Đang tra {canTra.Count} nhà cung cấp HĐĐT...");
                    using var sem2 = new System.Threading.SemaphoreSlim(4);
                    var t2 = new List<Task>();
                    foreach (var m0 in canTra)
                    {
                        string m = m0;
                        t2.Add(Task.Run(async () =>
                        {
                            await sem2.WaitAsync(Ct);
                            try
                            {
                                var (ten, _, _) = await _tct.TcnntLookupAsync(m, Ct);
                                lock (_nccTenCache)
                                {
                                    _nccTenCache[m] = ten ?? "";   // NHO CA KHI TRUOT -> khong tra lai
                                    if (!string.IsNullOrEmpty(ten)) Services.NccStore.Put(m, ten, "");
                                }
                            }
                            catch (OperationCanceledException) { }
                            catch { lock (_nccTenCache) _nccTenCache[m] = ""; }
                            finally
                            {
                                sem2.Release();
                                int n = System.Threading.Interlocked.Increment(ref dn);
                                Dispatcher.Invoke(() => SetProgress(n, canTra.Count, $"Đang tra nhà cung cấp {n}/{canTra.Count}..."));
                            }
                        }, Ct));
                    }
                    try { await Task.WhenAll(t2); } catch (OperationCanceledException) { }
                }

                // ===== GIAI DOAN 3: ghi Excel (KHONG goi mang nua -> chay rat nhanh) =====
                SetProgress(0, rows.Count, "Đang ghi file Excel...");
                int rT = headRowT + 1, rC = 2, i = 0;
                foreach (var row in rows)
                {
                    i++;
                    var hd = row.Raw!;
                    string nccCol = "", linkCol = "", maCol = "";
                    string dj = djs[i - 1], hdLienQuanCol = lqs[i - 1], ttLienQuanCol = ttlqs[i - 1];

                    int soDong = 0;   // dem so dong hang da ghi cho HD nay (0 -> ghi dong du phong)
                    try
                    {
                        if (!string.IsNullOrEmpty(dj))
                        {
                            var r = JsonDocument.Parse(dj).RootElement;
                            // Nha cung cap HDDT + link tra cuu + ma tra cuu (da tra san o GD2 -> tuc thi)
                            maCol = TimMaTraCuu(r);
                            var ncc = ResolveNccSync(ExS(r, "msttcgp"), hd.Nbmst, ExS(r, "id"), maCol);
                            nccCol = ncc.ten; linkCol = ncc.link;
                            if (r.TryGetProperty("hdhhdvu", out var arr) && arr.ValueKind == JsonValueKind.Array)
                                foreach (var it in arr.EnumerateArray())
                                {
                                    int tchat = (int)ExD(it, "tchat");         // 1=HHDV, 2=khuyen mai, 3=chiet khau, 4=ghi chu
                                    if (tchat == 4) continue;                  // dong dien giai/ghi chu: khong co tien
                                    double sign = tchat == 3 ? -1.0 : 1.0;     // chiet khau thuong mai -> TRU vao tong
                                    double thtien = ExD(it, "thtien");        // thanh tien chua thue
                                    double tsuat  = ExD(it, "tsuat");          // thue suat dang so (0.08)
                                    double tthue  = ExD(it, "tthue");          // tien thue/dong (hay null)
                                    if (tthue <= 0) tthue = Math.Round(thtien * tsuat, 0);  // null -> tu tinh
                                    thtien *= sign; tthue *= sign;             // dong chiet khau -> gia tri am
                                    wsC.Cell(rC, 1).Value  = hd.Khhdon;
                                    wsC.Cell(rC, 2).Value  = hd.Shdon;
                                    wsC.Cell(rC, 3).Value  = ExS(it, "stt");
                                    wsC.Cell(rC, 4).Value  = ExS(it, "ten");
                                    wsC.Cell(rC, 5).Value  = ExS(it, "dvtinh");
                                    wsC.Cell(rC, 6).Value  = ExD(it, "sluong");
                                    wsC.Cell(rC, 7).Value  = ExD(it, "dgia");        // don gia chua thue
                                    wsC.Cell(rC, 8).Value  = ExD(it, "tlckhau");     // ty le chiet khau %
                                    wsC.Cell(rC, 9).Value  = ExD(it, "stckhau");     // tien chiet khau
                                    wsC.Cell(rC, 10).Value = thtien;                 // thanh tien chua thue
                                    wsC.Cell(rC, 11).Value = ExS(it, "ltsuat");      // thue suat "8%"
                                    wsC.Cell(rC, 12).Value = tthue;                  // tien thue
                                    wsC.Cell(rC, 13).Value = thtien + tthue;         // thanh tien sau thue
                                    rC++; soDong++;
                                }
                        }
                    }
                    catch { }

                    // Khong lay duoc dong hang (TCT chan chi tiet) -> ghi 1 dong o muc hoa don
                    // de tong cot ChiTiet KHONG bi thieu so voi sheet TongHop.
                    if (soDong == 0)
                    {
                        wsC.Cell(rC, 1).Value  = hd.Khhdon;
                        wsC.Cell(rC, 2).Value  = hd.Shdon;
                        wsC.Cell(rC, 4).Value  = "(chưa lấy được chi tiết)";
                        wsC.Cell(rC, 10).Value = (double)hd.Tgtcthue;
                        wsC.Cell(rC, 12).Value = (double)hd.Tgtthue;
                        wsC.Cell(rC, 13).Value = (double)hd.Tgtttbso;
                        rC++;
                    }


                    wsT.Cell(rT, 1).Value = i;
                    wsT.Cell(rT, 2).Value = hd.Khmshdon;
                    wsT.Cell(rT, 3).Value = hd.Khhdon;
                    wsT.Cell(rT, 4).Value = hd.Shdon;
                    wsT.Cell(rT, 5).Value = row.NgayLap;
                    wsT.Cell(rT, 6).Value = row.LoaiHD;   // "Điện tử" / "Máy tính tiền"
                    wsT.Cell(rT, 7).Value = hd.Nbmst;
                    wsT.Cell(rT, 8).Value = hd.Nbten;
                    wsT.Cell(rT, 9).Value = hd.Nmmst;
                    wsT.Cell(rT, 10).Value = hd.Nmten;
                    wsT.Cell(rT, 11).Value = hd.Nmdchi;
                    wsT.Cell(rT, 12).Value = hd.Tgtcthue;
                    wsT.Cell(rT, 13).Value = hd.Tgtthue;
                    wsT.Cell(rT, 14).Value = hd.Ttcktmai;
                    wsT.Cell(rT, 15).Value = hd.Tgtphi;
                    wsT.Cell(rT, 16).Value = hd.Tgtttbso;
                    wsT.Cell(rT, 17).Value = hd.Dvtte;
                    wsT.Cell(rT, 18).Value = hd.Tgia;
                    wsT.Cell(rT, 19).Value = TrangThaiHD(hd.Tthai);
                    wsT.Cell(rT, 20).Value = KetQua(hd.Ttxly);
                    wsT.Cell(rT, 21).Value = nccCol;
                    wsT.Cell(rT, 22).Value = linkCol;
                    wsT.Cell(rT, 23).Value = maCol;
                    wsT.Cell(rT, 24).Value = hdLienQuanCol;
                    wsT.Cell(rT, 25).Value = ttLienQuanCol;
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

        // Mỗi nhà cung cấp HĐĐT đặt nhãn "mã tra cứu" một kiểu -> so khớp KHÔNG DẤU, không phân biệt hoa thường.
        // (a) Nhãn tiếng Việt tường minh — chắc chắn nhất.
        private static readonly string[] _nhanMaTraCuu =
        {
            "ma tra cuu", "matracuu", "ma so bi mat", "so bi mat", "ma bi mat",
            "so bao mat", "ma bao mat", "ma nhan hoa don", "ma xac thuc", "ma kiem tra"
        };
        // (b) Khóa kỹ thuật NCC dùng trong TTKhac (MISA=TransactionID, Viettel=reservationCode, ...).
        // So khớp CHÍNH XÁC (đã bỏ dấu, thường) để tránh nhận nhầm field khác.
        private static readonly string[] _khoaMaKyThuat =
        {
            "transactionid", "reservationcode", "fkey", "lookupcode", "securitycode", "sobaomat", "mabaomat"
        };

        /// <summary>Dò "Mã tra cứu" trong mảng cttkhac của JSON chi tiết (nhãn khác nhau tùy NCC).
        /// Đây là chìa khóa để vào cổng tra cứu của NCC tải PDF gốc.</summary>
        private static string TimMaTraCuu(JsonElement r)
        {
            if (!r.TryGetProperty("cttkhac", out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
            var pairs = new List<(string nhan, string dlieu)>();
            foreach (var it in arr.EnumerateArray())
            {
                string nhan = BoDau(ExS(it, "ttruong"));
                string dlieu = ExS(it, "dlieu").Trim();
                if (nhan.Length > 0 && dlieu.Length > 0) pairs.Add((nhan, dlieu));
            }
            // 1. Nhãn tiếng Việt tường minh -> 2. khóa kỹ thuật (TransactionID...)
            foreach (var p in pairs) if (_nhanMaTraCuu.Any(k => p.nhan.Contains(k))) return p.dlieu;
            foreach (var p in pairs) if (_khoaMaKyThuat.Contains(p.nhan)) return p.dlieu;
            return "";
        }

        /// <summary>Dò "Mã tra cứu" trong XML gốc (TTKhac/TTin) — CHÍNH XÁC hơn JSON detail vì XML
        /// luôn chứa đầy đủ thông tin khác đã ký, còn cttkhac trong JSON hay bị rỗng.
        /// Truyền msttcgp để xử lý đúng theo từng NCC (vd MISA: mã = TransactionID = thuộc tính Id của DLHDon).</summary>
        private static string TimMaTraCuuTuXml(string xml, string msttcgp = "")
        {
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var pairs = doc.Descendants().Where(e => e.Name.LocalName == "TTin")
                    .Select(tt => (
                        nhan: BoDau(tt.Elements().FirstOrDefault(e => e.Name.LocalName == "TTruong")?.Value ?? ""),
                        dlieu: (tt.Elements().FirstOrDefault(e => e.Name.LocalName == "DLieu")?.Value ?? "").Trim()))
                    .Where(p => p.nhan.Length > 0 && p.dlieu.Length > 0)
                    .ToList();

                // 1. Nhãn tiếng Việt tường minh -> 2. khóa kỹ thuật (MISA=TransactionID, Viettel=reservationCode...)
                foreach (var p in pairs) if (_nhanMaTraCuu.Any(k => p.nhan.Contains(k))) return p.dlieu;
                foreach (var p in pairs) if (_khoaMaKyThuat.Contains(p.nhan)) return p.dlieu;

                // 3. MISA: mã tra cứu = thuộc tính Id của DLHDon (= TransactionID, cũng nằm trong QR)
                if (msttcgp == "0101243150")
                {
                    string? id = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DLHDon")?.Attribute("Id")?.Value;
                    if (!string.IsNullOrWhiteSpace(id)) return id.Trim();
                }
            }
            catch { }
            return "";
        }

        /// <summary>Lấy nội dung XML text từ bytes export-xml (giải nén nếu là file zip "PK").</summary>
        private static string XmlTuBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            if (bytes.Length > 1 && bytes[0] == 0x50 && bytes[1] == 0x4B)   // "PK" = zip
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(bytes);
                    using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return "";
                    using var s = entry.Open();
                    using var sr = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
                    return sr.ReadToEnd();
                }
                catch { return ""; }
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Bỏ dấu tiếng Việt + hạ chữ thường để so khớp nhãn cho chắc.</summary>
        private static string BoDau(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string norm = s.ToLowerInvariant().Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(norm.Length);
            foreach (char c in norm)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
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
            ["0105987432"] = ("Công ty CP Đầu tư công nghệ EasyInvoice", "https://tracuu.easyinvoice.vn/Search/Index"),
            ["0106713804"] = ("Công ty Cổ phần Hilo Việt Nam", "https://tracuuhddt78.hilo.com.vn/"),
            ["0312303803"] = ("Công ty TNHH Win Tech Solution", "https://chungtu.wininvoice.vn/ct_ktt"),
        };

        // Nhớ tên NCC theo msttcgp trong PHIÊN — nhớ CẢ khi tra trượt (giá trị "")
        // để không lặp lại lời gọi tracuunnt ~18 giây cho từng hóa đơn.
        private readonly Dictionary<string, string> _nccTenCache = new();

        /// <summary>Bản KHÔNG gọi mạng của ResolveNccAsync — dùng lúc ghi Excel,
        /// vì các MST lạ đã được tra sẵn (song song, mỗi MST 1 lần) ở giai đoạn 2.</summary>
        private (string ten, string link) ResolveNccSync(string msttcgp, string nbmst, string id, string ma)
        {
            if (string.IsNullOrEmpty(msttcgp)) return ("", "");
            if (_tvanMap.TryGetValue(msttcgp, out var t))
                return ($"{msttcgp} - {t.ten}",
                        t.url.Replace("{id}", id).Replace("{nbmst}", nbmst).Replace("{ma}", ma));
            if (Services.NccStore.TryGet(msttcgp, out var cached) && !string.IsNullOrEmpty(cached.Ten))
                return ($"{msttcgp} - {cached.Ten}", cached.Link);
            if (_nccTenCache.TryGetValue(msttcgp, out var ten) && !string.IsNullOrEmpty(ten))
                return ($"{msttcgp} - {ten}", "");
            return (msttcgp, "");   // không tra được -> chỉ hiện MST
        }

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
            var (ten, _, _) = await _tct.TcnntLookupAsync(msttcgp, Ct);
            if (!string.IsNullOrEmpty(ten))
            {
                Services.NccStore.Put(msttcgp, ten, "");   // link rỗng: NCC ngoài bảng chưa có template tra cứu
                return ($"{msttcgp} - {ten}", "");
            }

            return (msttcgp, "");   // vẫn không ra -> chỉ hiện MST
        }

        // ===== Progress overlay + Hủy =====
        private System.Threading.CancellationTokenSource? _cts;

        private void ShowProgress(string text, bool indeterminate = false)
        {
            _cts = new System.Threading.CancellationTokenSource();
            btnCancel.IsEnabled = true;
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
            if (!Cancelled) txtProgress.Text = text;   // đã bấm Hủy -> giữ chữ "Đang hủy...", không ghi đè
        }
        private void HideProgress()
        {
            overlayProgress.Visibility = Visibility.Collapsed;
            _cts?.Dispose();
            _cts = null;
        }
        // True nếu người dùng đã bấm Hủy
        private bool Cancelled => _cts?.IsCancellationRequested ?? false;

        // Token để luồn xuống tầng HTTP -> bấm Hủy là cắt ngay cả khi đang chờ mạng/đang retry
        private System.Threading.CancellationToken Ct => _cts?.Token ?? System.Threading.CancellationToken.None;

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            btnCancel.IsEnabled = false;
            txtProgress.Text = "Đang hủy...";
        }

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
            string chieu = _loaiHD == "purchase" ? "đầu vào (mua vào)" : "đầu ra (bán ra)";
            ShowProgress($"🔐 Bước 1/2: Đang đăng nhập Tổng cục Thuế...", indeterminate: true);
            // Khai báo ngoài try để khi bấm Hủy vẫn giữ được phần đã tải
            var all = new List<HoaDonInfo>();
            try
            {
                _token = await _tct.LoginAsync(Session.Mst, Session.Password, Ct);
                _lastIsMuaVao = _loaiHD == "purchase";
                _lastLoai = _loaiHD;
                if (!Cancelled) txtProgress.Text = $"📥 Bước 2/2: Đang đồng bộ hóa đơn {chieu}...";

                // Chia khoảng ngày thành từng tháng (TCT giới hạn <= 1 tháng/lần) -> hỗ trợ cả Quý/Năm
                var chunkStart = dpTuNgay.SelectedDate.Value;
                var to = dpDenNgay.SelectedDate.Value;
                while (chunkStart <= to)
                {
                    if (Cancelled) break;
                    var chunkEnd = chunkStart.AddMonths(1).AddDays(-1);
                    if (chunkEnd > to) chunkEnd = to;
                    int soFar = all.Count;
                    string thang = chunkStart.ToString("MM/yyyy");
                    var part = await _tct.QueryInvoicesAsync(_token, _loaiHD,
                                   chunkStart.ToString("dd/MM/yyyy"), chunkEnd.ToString("dd/MM/yyyy"),
                                   (nguon, n) => { if (!Cancelled) txtProgress.Text = $"📥 Đang tải {nguon} tháng {thang}... (đã có {soFar + n})"; },
                                   Ct);
                    all.AddRange(part);
                    if (!Cancelled) txtProgress.Text = $"📥 Đang đồng bộ {chieu}... (đã có {all.Count})";
                    chunkStart = chunkEnd.AddDays(1);
                }

                FillGrid(all, _lastIsMuaVao);
                MessageBox.Show($"Đã tải {all.Count} hóa đơn {(_lastIsMuaVao ? "MUA VÀO" : "BÁN RA")} của {Session.TenDN}.",
                                "Đồng bộ xong", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                // Người dùng bấm Hủy giữa chừng — không phải lỗi
                FillGrid(all, _lastIsMuaVao);
                MessageBox.Show($"Đã hủy. Giữ lại {all.Count} hóa đơn đã tải được.",
                                "Đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
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
