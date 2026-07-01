using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;

namespace minhnhat_tool.Services
{
    /// <summary>Tự động cập nhật qua Velopack. Feed là 1 thư mục tĩnh trên IIS
    /// (chứa RELEASES, *.nupkg, Setup.exe). Đổi FeedUrl cho đúng server của bạn.</summary>
    public static class UpdateService
    {
        // 👉 ĐỔI URL NÀY = thư mục chứa bản phát hành trên IIS của bạn (có dấu / ở cuối)
        public const string FeedUrl = "https://decapcha.win-tech.vn/app/";

        /// <summary>Gọi 1 lần lúc app khởi động (không chặn UI). Có bản mới -> hỏi rồi cập nhật + restart.</summary>
        public static async Task CheckAsync(bool thongBaoKhiMoiNhat = false)
        {
            try
            {
                var mgr = new UpdateManager(FeedUrl);

                // Chạy từ IDE/exe lẻ (chưa cài qua Setup) -> bỏ qua, tránh lỗi.
                if (!mgr.IsInstalled) return;

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    if (thongBaoKhiMoiNhat)
                        MessageBox.Show("Bạn đang dùng phiên bản mới nhất.", "Cập nhật",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var ok = MessageBox.Show(
                    $"Đã có phiên bản mới {newVersion.TargetFullRelease.Version}.\nCập nhật ngay bây giờ?",
                    "Có bản cập nhật", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes) return;

                await mgr.DownloadUpdatesAsync(newVersion);
                // Cài đặt xong sẽ khởi động lại app ở phiên bản mới
                mgr.ApplyUpdatesAndRestart(newVersion);
            }
            catch (Exception ex)
            {
                if (thongBaoKhiMoiNhat)
                    MessageBox.Show("Không kiểm tra được cập nhật: " + ex.Message, "Cập nhật",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
