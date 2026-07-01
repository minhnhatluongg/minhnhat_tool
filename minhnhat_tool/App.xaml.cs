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
            base.OnStartup(e);
        }
    }
}
