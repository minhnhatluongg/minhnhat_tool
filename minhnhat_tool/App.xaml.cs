using System.Windows;
using Velopack;

namespace minhnhat_tool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // BẮT BUỘC: chạy đầu tiên để Velopack xử lý các bước cài/gỡ/cập nhật rồi mới mở UI
            VelopackApp.Build().Run();

            // Ngày tháng theo kiểu Việt Nam (dd/MM/yyyy) BẤT KỂ Windows đang đặt ngôn ngữ gì.
            // Máy cài Windows tiếng Anh sẽ hiện 8/19/2026 — kế toán đọc nhầm ngày là chuyện lớn.
            var vi = new System.Globalization.CultureInfo("vi-VN");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = vi;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = vi;
            System.Threading.Thread.CurrentThread.CurrentCulture = vi;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("vi-VN")));

            base.OnStartup(e);
        }
    }
}
