using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    public partial class CaiDatWindow : Window
    {
        private CancellationTokenSource? _cts;

        public CaiDatWindow()
        {
            InitializeComponent();
            for (int h = 0; h < 24; h++) cboGio.Items.Add(h.ToString("00") + ":00");

            var st = CaoNenSettings.HienTai;
            chkBat.IsChecked = st.Bat;
            cboGio.SelectedIndex = Math.Max(0, Math.Min(23, st.GioChay));
            txtSoNgay.Text = st.SoNgay.ToString();

            CapNhatTrangThai();
            CapNhatCache();
        }

        private void CapNhatTrangThai()
        {
            var st = CaoNenSettings.HienTai;
            lblTrangThai.Text = string.IsNullOrEmpty(st.LanChayCuoi)
                ? "Chưa cào nền lần nào."
                : $"Lần cào nền gần nhất: {st.LanChayCuoi} — đã làm ấm {st.SoHoaDonLanCuoi} hóa đơn.";
        }

        private void CapNhatCache()
        {
            var (d, x, mb) = HoaDonCache.ThongKe();
            lblCache.Text = $"Đang lưu chi tiết của {d} hóa đơn, {x} file XML — {mb:F1} MB.\n" +
                            "Xóa cache là an toàn: dữ liệu sẽ tự tải lại từ Tổng cục Thuế khi cần.";
        }

        private void LuuCaiDat()
        {
            var st = CaoNenSettings.HienTai;
            st.Bat = chkBat.IsChecked == true;
            st.GioChay = Math.Max(0, cboGio.SelectedIndex);
            st.SoNgay = int.TryParse(txtSoNgay.Text.Trim(), out var n) && n > 0 ? n : 45;
            st.Luu();
        }

        private async void btnChayNgay_Click(object sender, RoutedEventArgs e)
        {
            LuuCaiDat();
            if (CaoNenService.DangChay) { MessageBox.Show("Cào nền đang chạy rồi."); return; }
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
                await CaoNenService.RunAsync(
                    msg => Dispatcher.Invoke(() => lblTrangThai.Text = msg), _cts.Token);
            }
            catch (OperationCanceledException) { lblTrangThai.Text = "Đã dừng cào nền."; }
            catch (Exception ex) { lblTrangThai.Text = "Lỗi cào nền: " + ex.Message; }
            finally
            {
                btnChayNgay.IsEnabled = true; btnDungCao.IsEnabled = false;
                _cts?.Dispose(); _cts = null;
                CapNhatTrangThai(); CapNhatCache();
            }
        }

        private void btnDungCao_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void btnXoaCache_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Xóa toàn bộ cache dữ liệu hóa đơn trên máy?\nLần xuất sau sẽ tải lại từ TCT (chậm hơn).",
                    "Xóa cache", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            HoaDonCache.Xoa();
            CapNhatCache();
            MessageBox.Show("Đã xóa cache.");
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            LuuCaiDat();
            base.OnClosed(e);
        }
    }
}
