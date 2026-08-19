using System;
using System.Windows;
using System.Windows.Media;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    public partial class LoginWindow : Window
    {
        private readonly HoaDonDienTuClient _tct = new();

        public LoginWindow()
        {
            InitializeComponent();
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) txtPhienBan.Text = $"Phiên bản {v.Major}.{v.Minor}.{v.Build}";
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string mst = txtMst.Text.Trim();
            string pwd = txtPassword.Password;   // PasswordBox dùng .Password (không phải .Text)

            if (string.IsNullOrEmpty(mst) || string.IsNullOrEmpty(pwd))
            {
                ShowStatus("Nhập đủ Mã số thuế và mật khẩu!", isError: true);
                return;
            }

            btnLogin.IsEnabled = false;
            ShowStatus("Đang giải captcha & đăng nhập...", isError: false);
            try
            {
                // Vượt captcha tự động + đăng nhập hoadondientu -> lấy token
                string token = await _tct.LoginAsync(mst, pwd);

                // Lưu phiên để MainWindow + worker dùng
                Session.Mst = mst;
                Session.Password = pwd;
                Session.Token = token;

                // Mở màn hình chính, đóng form đăng nhập
                new MainWindow().Show();
                this.Close();
            }
            catch (Exception ex)
            {
                ShowStatus("Đăng nhập thất bại: " + ex.Message, isError: true);
                btnLogin.IsEnabled = true;
            }
        }

        /// <summary>Đổi cả chữ lẫn icon dòng trạng thái — icon báo tình huống nhanh hơn đọc chữ.</summary>
        private void ShowStatus(string msg, bool isError)
        {
            txtStatus.Text = msg;
            txtStatus.Foreground = isError ? Brushes.Tomato : Brushes.LightGray;
            icoStatus.Stroke = isError ? Brushes.Tomato : Brushes.LightGray;
            icoStatus.Data = (Geometry)FindResource(isError ? "IcCanhBao" : "IcDangTai");
        }
    }
}
