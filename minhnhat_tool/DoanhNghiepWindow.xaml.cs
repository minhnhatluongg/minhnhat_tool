using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using minhnhat_tool.Models;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    public partial class DoanhNghiepWindow : Window
    {
        private List<DoanhNghiep> _list;
        private readonly HoaDonDienTuClient _tct = new HoaDonDienTuClient();

        public DoanhNghiepWindow()
        {
            InitializeComponent();
            _list = DoanhNghiepStore.Load();
            RefreshList();
        }

        // Rời ô MST -> tự tra tên DN (cache trước, không có mới gọi API tracuunnt)
        private async void txtMst_LostFocus(object sender, RoutedEventArgs e)
        {
            string mst = txtMst.Text.Trim();
            if (string.IsNullOrEmpty(mst)) { txtTen.Text = ""; return; }

            // Đã lưu trong danh sách -> lấy tên sẵn có
            var existing = _list.FirstOrDefault(x => x.Mst == mst);
            if (existing != null && !string.IsNullOrEmpty(existing.TenDN)) { txtTen.Text = existing.TenDN; return; }

            // Có trong cache NCC -> tức thì
            if (NccStore.TryGet(mst, out var cached) && !string.IsNullOrEmpty(cached.Ten)) { txtTen.Text = cached.Ten; return; }

            // Chưa biết -> gọi API (có anti-bot nên hơi lâu)
            txtTen.Text = "Đang tra tên doanh nghiệp...";
            IsEnabled = false;
            try
            {
                var (ten, _, _) = await _tct.TcnntLookupAsync(mst);
                if (!string.IsNullOrEmpty(ten)) { txtTen.Text = ten; NccStore.Put(mst, ten, ""); }
                else txtTen.Text = "(Không tra được tên — kiểm tra lại MST)";
            }
            catch { txtTen.Text = "(Lỗi tra tên)"; }
            finally { IsEnabled = true; }
        }

        private void RefreshList()
        {
            lstDN.ItemsSource = null;
            lstDN.ItemsSource = _list;
        }

        // Click 1 DN trong danh sách -> đổ lên form để sửa
        private void lstDN_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstDN.SelectedItem is DoanhNghiep dn)
            {
                txtMst.Text = dn.Mst;
                txtTen.Text = dn.TenDN;
                txtPassword.Password = dn.Password;
            }
        }

        private void btnLuu_Click(object sender, RoutedEventArgs e)
        {
            string mst = txtMst.Text.Trim();
            if (string.IsNullOrEmpty(mst) || string.IsNullOrEmpty(txtPassword.Password))
            {
                MessageBox.Show("Nhập đủ Mã số thuế và mật khẩu!");
                return;
            }

            // Tên tự tra: bỏ qua các text tạm ("Đang tra...", "(Không tra được...)")
            string ten = txtTen.Text.Trim();
            if (ten.StartsWith("(") || ten.StartsWith("Đang tra")) ten = "";

            // Có MST rồi -> cập nhật, chưa có -> thêm mới
            var existing = _list.FirstOrDefault(x => x.Mst == mst);
            if (existing != null)
            {
                existing.TenDN = ten;
                existing.Password = txtPassword.Password;
            }
            else
            {
                _list.Add(new DoanhNghiep { Mst = mst, TenDN = ten, Password = txtPassword.Password });
            }

            DoanhNghiepStore.Save(_list);
            RefreshList();
            MessageBox.Show("Đã lưu doanh nghiệp.");
        }

        private void btnXoa_Click(object sender, RoutedEventArgs e)
        {
            if (lstDN.SelectedItem is DoanhNghiep dn)
            {
                _list.Remove(dn);
                DoanhNghiepStore.Save(_list);
                RefreshList();
                ClearForm();
            }
            else
            {
                MessageBox.Show("Chọn doanh nghiệp cần xóa trong danh sách.");
            }
        }

        private void btnLamMoi_Click(object sender, RoutedEventArgs e) => ClearForm();

        private void ClearForm()
        {
            lstDN.SelectedItem = null;
            txtMst.Text = "";
            txtTen.Text = "";
            txtPassword.Password = "";
        }

        // Chọn DN này -> lưu vào phiên, đóng cửa sổ
        private void btnChon_Click(object sender, RoutedEventArgs e)
        {
            if (lstDN.SelectedItem is DoanhNghiep dn)
            {
                Session.Mst = dn.Mst;
                Session.TenDN = dn.TenDN;
                Session.Password = dn.Password;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Chọn 1 doanh nghiệp trong danh sách trước.");
            }
        }
    }
}
