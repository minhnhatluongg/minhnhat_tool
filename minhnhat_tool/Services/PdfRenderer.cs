using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace minhnhat_tool.Services
{
    /// <summary>Dựng PDF từ HTML bằng WebView2 chạy ngầm (runtime có sẵn trên Windows 10/11).
    /// Khởi tạo MỘT lần rồi tái dùng cho cả lô -> nhanh hơn nhiều so với mở/đóng từng tờ.</summary>
    public sealed class PdfRenderer : IAsyncDisposable
    {
        private Window? _host;
        private WebView2? _web;
        private bool _ready;

        public async Task InitAsync()
        {
            if (_ready) return;

            // Thư mục dữ liệu riêng: tránh lỗi khi thư mục cài đặt không cho ghi
            string userData = Path.Combine(Path.GetTempPath(), "minhnhat_tool_wv2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);

            _web = new WebView2();
            // Cửa sổ đặt ngoài màn hình chỉ để WebView2 có HWND; người dùng không nhìn thấy
            _host = new Window
            {
                Width = 900,
                Height = 1200,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -32000,
                Top = -32000,
                Content = _web
            };
            _host.Show();

            await _web.EnsureCoreWebView2Async(env);
            _ready = true;
        }

        /// <summary>Render 1 trang HTML ra file PDF. Trả về true nếu ghi được file.</summary>
        public async Task<bool> HtmlToPdfAsync(string html, string outPath)
        {
            if (!_ready) await InitAsync();
            var core = _web?.CoreWebView2;
            if (core == null) return false;

            var done = new TaskCompletionSource<bool>();
            void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
            core.NavigationCompleted += OnDone;
            try
            {
                core.NavigateToString(html);
                // Chờ nạp xong, tối đa 30s để 1 tờ lỗi không treo cả lô
                if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(30))) != done.Task)
                    return false;
            }
            finally { core.NavigationCompleted -= OnDone; }

            var st = core.Environment.CreatePrintSettings();
            st.ShouldPrintBackgrounds = true;
            st.ShouldPrintHeaderAndFooter = false;
            st.PageWidth = 8.27; st.PageHeight = 11.69;      // A4 (inch)
            st.MarginTop = st.MarginBottom = 0.2;
            st.MarginLeft = st.MarginRight = 0.2;

            return await core.PrintToPdfAsync(outPath, st);
        }

        /// <summary>Nạp HTML, GIẢ LẬP ngữ cảnh IN khổ A4 (media=print + bề ngang vùng in 190mm),
        /// rồi chạy JS đo đạc. Dùng để kiểm tra bố cục có tràn lề khi in hay không.</summary>
        public async Task<string> DoBoCucKhiInAsync(string html, string js)
        {
            if (!_ready) await InitAsync();
            var core = _web?.CoreWebView2;
            if (core == null) return "";

            var done = new TaskCompletionSource<bool>();
            void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
            core.NavigationCompleted += OnDone;
            try
            {
                core.NavigateToString(html);
                if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(30))) != done.Task) return "";
            }
            finally { core.NavigationCompleted -= OnDone; }

            // A4 rộng 210mm, trừ lề 10mm mỗi bên -> vùng in 190mm ≈ 718px @96dpi
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                "{\"width\":718,\"height\":1000,\"deviceScaleFactor\":1,\"mobile\":false}");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"media\":\"print\"}");

            string raw = await core.ExecuteScriptAsync(js);
            if (string.IsNullOrEmpty(raw) || raw == "null") return "";
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch { return raw.Trim('"'); }
        }

        public ValueTask DisposeAsync()
        {
            try { _web?.Dispose(); } catch { }
            try { _host?.Close(); } catch { }
            _web = null; _host = null; _ready = false;
            return ValueTask.CompletedTask;
        }
    }
}
