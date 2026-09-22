using System;
using System.Windows;
using minhnhat_tool.Services;

namespace minhnhat_tool.Ui
{
    /// <summary>
    /// Hộp thoại lỗi dùng chung cho cả app.
    ///
    ///     Ui.LoiDialog.Show(this, "Không tải được hóa đơn", ex);
    ///
    /// Câu ngắn (ex.Message) hiện ở trên; phần kỹ thuật (mã HTTP + JSON thô của TCT với TctException,
    /// stack trace với lỗi khác) gập lại trong "Chi tiết kỹ thuật", có nút sao chép để gửi hỗ trợ.
    /// </summary>
    public partial class LoiDialog : Window
    {
        private readonly string _chiTiet;

        private LoiDialog(string tieuDe, string thongBao, string chiTiet)
        {
            InitializeComponent();
            Title = tieuDe;
            txtTieuDe.Text = tieuDe;
            txtThongBao.Text = thongBao;
            _chiTiet = chiTiet;
            txtChiTiet.Text = chiTiet;

            // Không có gì để xem thêm thì giấu luôn nút gập và nút sao chép
            var co = string.IsNullOrWhiteSpace(chiTiet) ? Visibility.Collapsed : Visibility.Visible;
            btnChiTiet.Visibility = co;
            btnSaoChep.Visibility = co;
        }

        /// <summary>Hiện lỗi từ Exception. TctException: câu dễ hiểu + chi tiết đã tách sẵn; lỗi khác: Message + stack.</summary>
        public static void Show(Window? owner, string tieuDe, Exception ex)
        {
            string chiTiet = ex is TctException t ? t.ChiTiet : ex.ToString();
            Show(owner, tieuDe, ex.Message, chiTiet);
        }

        /// <summary>Hiện lỗi với câu tự soạn; <paramref name="chiTiet"/> rỗng thì không có mục gập.</summary>
        public static void Show(Window? owner, string tieuDe, string thongBao, string chiTiet = "")
        {
            var dlg = new LoiDialog(tieuDe, thongBao, chiTiet);
            if (owner != null && owner.IsLoaded) dlg.Owner = owner;
            else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.ShowDialog();
        }

        private void btnSaoChep_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtTieuDe.Text + Environment.NewLine + txtThongBao.Text +
                                  Environment.NewLine + Environment.NewLine + _chiTiet);
                btnSaoChep.Content = "Đã sao chép";
            }
            catch { }   // clipboard bị app khác giữ -> bỏ qua, không làm hộp lỗi lại nổ lỗi
        }

        private void btnDong_Click(object sender, RoutedEventArgs e) => Close();
    }
}
