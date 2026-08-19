using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    public partial class CaiDatWindow : Window
    {
        private static readonly System.Windows.Media.Brush Xanh =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xd3, 0xee));
        private static readonly System.Windows.Media.Brush Xam =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1e, 0x29, 0x3b));

        private CancellationTokenSource? _cts;
        private bool _dangNap = true;   // chặn các sự kiện Changed bắn lúc đang đổ dữ liệu vào form

        public CaiDatWindow()
        {
            InitializeComponent();
            for (int h = 0; h < 24; h++) cboGio.Items.Add(h.ToString("00") + ":00");

            var st = CaoNenSettings.HienTai;
            chkBat.IsChecked = st.Bat;
            cboGio.SelectedIndex = Math.Max(0, Math.Min(23, st.GioChay));
            chkMuaVao.IsChecked = st.CaoMuaVao;
            chkBanRa.IsChecked = st.CaoBanRa;
            chkXml.IsChecked = st.LayXml;
            txtSoNgay.Text = st.SoNgay.ToString();
            rdCoDinh.IsChecked = st.PhamVi == "CoDinh";
            rdGanDay.IsChecked = st.PhamVi != "CoDinh";
            var (tuMd, denMd) = st.KhoangYeuCau();
            dpTu.SelectedDate = CaoNenSettings.ParseNgay(st.TuNgay) ?? tuMd;
            dpDen.SelectedDate = CaoNenSettings.ParseNgay(st.DenNgay) ?? denMd;

            var tx = TaxInfoSettings.HienTai;
            txtApiKey.Text = tx.ApiKey;
            txtApiSecret.Text = tx.ApiSecret;
            chkTuKiemTra.IsChecked = tx.TuKiemTraSauDongBo;

            _dangNap = false;
            CapNhatTrangThai();
            CapNhatPhamVi();
            CapNhatKhoa();
            NapThongKe();
            _ = NapHanMucAsync();
        }

        // ===================== TAB CÀO NỀN =====================

        private void PhamVi_Changed(object sender, RoutedEventArgs e) => CapNhatPhamVi();
        private void Khoang_Changed(object sender, RoutedEventArgs e) => CapNhatPhamVi();
        private void Ngay_Changed(object sender, SelectionChangedEventArgs e) => CapNhatPhamVi();

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            rdGanDay.IsChecked = true;
            txtSoNgay.Text = tag;
            CapNhatPhamVi();
        }

        /// <summary>Chọn nhanh một kỳ kế toán cho chế độ khoảng cố định — quyết toán năm hay soát
        /// xét quý là hai việc hay phải cào lại lịch sử nhất.</summary>
        private void PresetKy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            var hnay = DateTime.Today;
            DateTime tu, den;
            switch (tag)
            {
                case "namnay": tu = new DateTime(hnay.Year, 1, 1); den = hnay; break;
                case "namngoai": tu = new DateTime(hnay.Year - 1, 1, 1); den = new DateTime(hnay.Year - 1, 12, 31); break;
                case "quynay":
                    tu = new DateTime(hnay.Year, (hnay.Month - 1) / 3 * 3 + 1, 1);
                    den = hnay;
                    break;
                case "quytruoc":
                    var dauQuyNay = new DateTime(hnay.Year, (hnay.Month - 1) / 3 * 3 + 1, 1);
                    tu = dauQuyNay.AddMonths(-3);
                    den = dauQuyNay.AddDays(-1);
                    break;
                default: return;
            }
            rdCoDinh.IsChecked = true;
            dpTu.SelectedDate = tu;
            dpDen.SelectedDate = den;
            CapNhatPhamVi();
        }

        /// <summary>Bật/tắt đúng nhóm ô nhập theo phạm vi đang chọn, và nói thẳng khoảng ngày SẼ chạy
        /// (sau khi áp trần của gói) để người dùng không phải đoán.</summary>
        private void CapNhatPhamVi()
        {
            if (_dangNap) return;

            bool ganDay = rdGanDay.IsChecked == true;
            txtSoNgay.IsEnabled = ganDay;
            dpTu.IsEnabled = dpDen.IsEnabled = !ganDay;

            // Làm nổi thẻ đang chọn: hai lựa chọn loại trừ nhau nên phải nhìn là biết cái nào đang chạy.
            brdGanDay.BorderBrush = ganDay ? Xanh : Xam;
            brdCoDinh.BorderBrush = ganDay ? Xam : Xanh;
            brdGanDay.Opacity = ganDay ? 1.0 : 0.55;
            brdCoDinh.Opacity = ganDay ? 0.55 : 1.0;

            var st = DocForm();
            var (tu, den) = st.KhoangYeuCau();

            if (!TaxInfoSettings.HienTai.DaCauHinh)
            {
                brdCanhBao.Visibility = Visibility.Visible;
                lblGioiHan.Text = "Chưa nhập khóa API nên cào nền sẽ không chạy — tính năng này tính phí " +
                                  "theo tờ hóa đơn. Nhập khóa ở tab \"Kiểm tra NCC\", xem số tờ còn lại " +
                                  "ở tab \"Hạn mức\".";
            }
            else brdCanhBao.Visibility = Visibility.Collapsed;

            int soNgay = (den - tu).Days + 1;
            string chieu = st.MoTaLoai();
            lblUocLuong.Text =
                $"Sẽ cào: {chieu}{(st.LayXml ? " (kèm XML gốc)" : "")} — từ {tu:dd/MM/yyyy} đến {den:dd/MM/yyyy} ({soNgay} ngày).\n" +
                "Chỉ tờ CHƯA có trên máy mới tốn hạn mức, và mỗi tờ chỉ tính tiền một lần — chạy lại " +
                "khoảng ngày cũ gần như không mất gì. Mỗi tờ tải mới mất khoảng 1 giây (1.000 tờ ≈ 17 phút).";
        }

        /// <summary>Đọc nguyên trạng thái form ra một CaoNenSettings (chưa lưu xuống đĩa).</summary>
        private CaoNenSettings DocForm()
        {
            var st = CaoNenSettings.HienTai;
            st.Bat = chkBat.IsChecked == true;
            st.GioChay = Math.Max(0, cboGio.SelectedIndex);
            st.CaoMuaVao = chkMuaVao.IsChecked == true;
            st.CaoBanRa = chkBanRa.IsChecked == true;
            st.LayXml = chkXml.IsChecked == true;
            st.PhamVi = rdCoDinh.IsChecked == true ? "CoDinh" : "GanDay";
            st.SoNgay = int.TryParse(txtSoNgay.Text.Trim(), out var n) && n > 0 ? n : 45;
            st.TuNgay = dpTu.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
            st.DenNgay = dpDen.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
            return st;
        }

        private void LuuCaiDat() { if (!_dangNap) DocForm().Luu(); }

        private void CapNhatTrangThai()
        {
            var st = CaoNenSettings.HienTai;
            lblTrangThai.Text = string.IsNullOrEmpty(st.TomTatLanCuoi)
                ? (string.IsNullOrEmpty(st.LanChayCuoi)
                    ? "Chưa cào nền lần nào."
                    : $"Lần cào nền gần nhất: {st.LanChayCuoi} — đã làm ấm {st.SoHoaDonLanCuoi} hóa đơn.")
                : "Lần chạy gần nhất — " + st.TomTatLanCuoi;
        }

        private async void btnChayNgay_Click(object sender, RoutedEventArgs e)
        {
            LuuCaiDat();
            if (CaoNenService.DangChay) { MessageBox.Show("Cào nền đang chạy rồi."); return; }

            var st = CaoNenSettings.HienTai;
            if (st.CacLoai().Length == 0)
            {
                MessageBox.Show("Chưa chọn cào hóa đơn đầu vào hay đầu ra.", "Cào nền",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var ds = DoanhNghiepStore.Load();
            if (ds.TrueForAll(c => string.IsNullOrWhiteSpace(c.Password)))
            {
                MessageBox.Show("Chưa có công ty nào lưu mật khẩu. Vào 'Quản lý doanh nghiệp' lưu mật khẩu trước.");
                return;
            }

            _cts = new CancellationTokenSource();
            btnChayNgay.IsEnabled = false; btnDungCao.IsEnabled = true;
            try
            {
                var kq = await CaoNenService.RunAsync(
                    msg => Dispatcher.Invoke(() => lblTrangThai.Text = msg), _cts.Token);

                if (kq.CanhBao.Count > 0)
                    MessageBox.Show(kq.TomTat + "\n\nLưu ý:\n• " + string.Join("\n• ", kq.CanhBao.Take(12)),
                        "Cào nền xong", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException) { lblTrangThai.Text = "Đã dừng cào nền."; }
            catch (Exception ex) { lblTrangThai.Text = "Lỗi cào nền: " + ex.Message; }
            finally
            {
                btnChayNgay.IsEnabled = true; btnDungCao.IsEnabled = false;
                _cts?.Dispose(); _cts = null;
                CapNhatTrangThai(); NapThongKe();
            }
        }

        private void btnDungCao_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        // ===================== TAB THỐNG KÊ =====================

        private void tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_dangNap || !ReferenceEquals(e.OriginalSource, tabs)) return;
            if (tabs.SelectedIndex == 1) NapThongKe();
            if (tabs.SelectedIndex == 3) { CapNhatKhoa(); _ = NapHanMucAsync(); }
        }

        private void btnLamMoi_Click(object sender, RoutedEventArgs e) => NapThongKe();

        private void NapThongKe()
        {
            var ten = DoanhNghiepStore.Load()
                .Where(c => !string.IsNullOrWhiteSpace(c.Mst))
                .GroupBy(c => c.Mst.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().TenDN ?? "", StringComparer.OrdinalIgnoreCase);

            var rows = HoaDonCache.ThongKeTheoCty();
            foreach (var r in rows)
                r.Ten = ten.TryGetValue(r.Mst, out var t) && !string.IsNullOrWhiteSpace(t) ? t : "(chưa lưu tên)";
            grdCty.ItemsSource = rows;

            var (soHd, coCt, coXml, tu, den) = HoaDonCache.TongQuanKho();
            var (d, x, mb) = HoaDonCache.ThongKe();

            lblTongQuan.Text = soHd == 0
                ? "Kho đang trống — chạy Đồng bộ hoặc Cào nền để có dữ liệu."
                : $"{soHd:N0} hóa đơn trong kho" +
                  (string.IsNullOrEmpty(tu) ? "" : $" · từ {tu} đến {den}") +
                  $"\n{coCt:N0} tờ đã có chi tiết ({Ptram(coCt, soHd)}) · {coXml:N0} tờ đã có XML gốc ({Ptram(coXml, soHd)})" +
                  (soHd > coCt ? $"\nCòn {soHd - coCt:N0} tờ chưa có chi tiết — lần xuất Excel sau sẽ tự tải phần này (chậm hơn)." : "");

            lblCache.Text = $"File dữ liệu: {mb:F1} MB (đang giữ chi tiết của {d:N0} hóa đơn và {x:N0} file XML). " +
                            "Toàn bộ nằm trên máy này. Xóa kho là an toàn: dữ liệu sẽ tự tải lại từ Tổng cục Thuế khi cần.";

            grdThang.ItemsSource = null;
            lblThang.Text = "Chi tiết theo tháng — bấm một dòng bên trái";
        }

        private static string Ptram(int a, int b) => b == 0 ? "0%" : $"{a * 100.0 / b:F0}%";

        private void grdCty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (grdCty.SelectedItem is not HoaDonCache.DongKho d) { grdThang.ItemsSource = null; return; }
            grdThang.ItemsSource = HoaDonCache.ThongKeTheoThang(d.Mst, d.LoaiRaw);
            lblThang.Text = $"{d.Mst} — {d.Loai}, theo tháng";
        }

        private void btnXoaCty_Click(object sender, RoutedEventArgs e)
        {
            if (grdCty.SelectedItem is not HoaDonCache.DongKho d)
            { MessageBox.Show("Chọn một dòng công ty trước."); return; }

            if (MessageBox.Show(
                    $"Xóa toàn bộ dữ liệu đã tải của {d.Mst} trên máy này?\n" +
                    "Hóa đơn dùng chung với công ty khác sẽ được giữ lại.\n" +
                    "Lần xuất sau sẽ tải lại từ Tổng cục Thuế (chậm hơn).",
                    "Xóa dữ liệu công ty", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            HoaDonCache.XoaTheoCty(d.Mst);
            NapThongKe();
        }

        private void btnXoaCache_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Xóa toàn bộ kho dữ liệu hóa đơn trên máy?\nLần xuất sau sẽ tải lại từ TCT (chậm hơn).",
                    "Xóa kho", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            HoaDonCache.Xoa();
            NapThongKe();
            MessageBox.Show("Đã xóa kho dữ liệu.");
        }

        // ===================== TAB KIỂM TRA NCC =====================

        /// <summary>Thử gọi dịch vụ tra cứu bằng khóa vừa nhập — xác nhận khóa dùng được
        /// trước khi tin vào kết quả kiểm tra nhà cung cấp.</summary>
        private async void btnThuTraCuu_Click(object sender, RoutedEventArgs e)
        {
            LuuTraCuu();
            var tx = TaxInfoSettings.HienTai;
            if (!tx.DaCauHinh) { lblTraCuu.Text = "Chưa nhập API Key / Secret."; return; }

            btnThuTraCuu.IsEnabled = false;
            lblTraCuu.Text = "Đang thử...";
            try
            {
                string mst = string.IsNullOrWhiteSpace(Session.Mst) ? "0312303803" : Session.Mst;
                var kq = await new TaxInfoClient(tx).TraAsync(new[] { mst });
                if (kq.Items.Count > 0)
                {
                    var it = kq.Items[0];
                    lblTraCuu.Text = it.ThanhCong
                        ? $"OK — {it.Mst}: {it.HienThi}" + (string.IsNullOrEmpty(it.Ten) ? "" : $" ({it.Ten})")
                        : $"Tra được nhưng MST {it.Mst} lỗi: {it.ThongBao}";
                }
                else lblTraCuu.Text = string.IsNullOrEmpty(kq.ThongBao) ? "Không có kết quả." : kq.ThongBao;
            }
            catch (Exception ex) { lblTraCuu.Text = "Lỗi: " + ex.Message; }
            finally { btnThuTraCuu.IsEnabled = true; }
        }

        private void LuuTraCuu()
        {
            var tx = TaxInfoSettings.HienTai;
            tx.ApiKey = txtApiKey.Text.Trim();
            tx.ApiSecret = txtApiSecret.Text.Trim();
            tx.TuKiemTraSauDongBo = chkTuKiemTra.IsChecked == true;
            tx.Luu();
        }

        // ===================== TAB HẠN MỨC =====================

        private void CapNhatKhoa()
        {
            var tx = TaxInfoSettings.HienTai;
            int daTra = HoaDonCache.DemDaTra();
            lblKhoaTomTat.Text = !tx.DaCauHinh
                ? "Chưa nhập khóa API."
                : $"Đang dùng khóa {ChePhanCuoi(tx.ApiKey)} tại {tx.BaseUrl}.\n" +
                  $"Máy này đã ghi nhận {daTra:N0} tờ đã trả tiền — những tờ đó tải lại không mất thêm, " +
                  "kể cả khi mất mạng.";
        }

        private static string ChePhanCuoi(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= 10 ? s : s.Substring(0, 10) + "…");

        private async void btnLamMoiHanMuc_Click(object sender, RoutedEventArgs e) => await NapHanMucAsync();

        /// <summary>Hỏi máy chủ số tờ còn lại. Không tốn hạn mức.</summary>
        private async Task NapHanMucAsync()
        {
            var tx = TaxInfoSettings.HienTai;
            if (!tx.DaCauHinh)
            {
                lblHanMuc.Text = "Chưa nhập khóa";
                lblHanMucPhu.Text = "Nhập API Key / Secret ở tab \"Kiểm tra NCC\" để dùng được cào nền.";
                lblHanMucChiTiet.Text = "";
                lblHanMucLoi.Text = "";
                pbHanMuc.Value = 0;
                return;
            }

            btnLamMoiHanMuc.IsEnabled = false;
            lblHanMucLoi.Text = "Đang hỏi máy chủ...";
            try
            {
                var hm = await new QuotaClient(tx).ThongTinAsync();
                if (!hm.ThanhCong)
                {
                    lblHanMuc.Text = "Không xem được";
                    lblHanMucPhu.Text = "";
                    lblHanMucChiTiet.Text = "";
                    lblHanMucLoi.Text = hm.ThongBao;
                    pbHanMuc.Value = 0;
                    return;
                }

                lblHanMucLoi.Text = "";
                lblHanMuc.Text = $"{hm.ConLai:N0} tờ";
                lblHanMucPhu.Text = $"{hm.CongTy} — MST {hm.Mst}" +
                                    (hm.DangHoatDong ? "" : "   ⚠ khóa đang bị tạm khóa");
                lblHanMucChiTiet.Text = $"Đã dùng {hm.DaDung:N0} / {hm.TongSoTo:N0} tờ.";
                pbHanMuc.Value = hm.TongSoTo > 0 ? Math.Min(100, hm.DaDung * 100.0 / hm.TongSoTo) : 0;
                pbHanMuc.Foreground = hm.ConLai <= 0
                    ? System.Windows.Media.Brushes.IndianRed
                    : (hm.TongSoTo > 0 && hm.DaDung * 1.0 / hm.TongSoTo > 0.8
                        ? System.Windows.Media.Brushes.Orange
                        : System.Windows.Media.Brushes.MediumSeaGreen);
            }
            catch (Exception ex) { lblHanMucLoi.Text = "Lỗi: " + ex.Message; }
            finally { btnLamMoiHanMuc.IsEnabled = true; }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            LuuCaiDat();
            LuuTraCuu();
            base.OnClosed(e);
        }
    }
}
