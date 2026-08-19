using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace minhnhat_tool.Services
{
    /// <summary>Tình trạng một người nộp thuế.</summary>
    public class TinhTrangNnt
    {
        public string Mst { get; set; } = "";
        public bool ThanhCong { get; set; }
        public string Ten { get; set; } = "";
        /// <summary>Mã 2 ký tự: "00"/"04" = đang hoạt động, còn lại là bất thường.</summary>
        public string Ma { get; set; } = "";
        public string MoTa { get; set; } = "";
        public string ThongBao { get; set; } = "";

        /// <summary>RỦI RO = không ở trạng thái đang hoạt động bình thường.
        /// "00" đang hoạt động, "04" đang hoạt động (hộ kinh doanh/cá nhân) -> KHÔNG rủi ro.
        /// Đáng lưu ý nhất là "06" (không hoạt động tại địa chỉ đã đăng ký) — dấu hiệu hóa đơn khống.</summary>
        public bool RuiRo => ThanhCong && Ma != "00" && Ma != "04";

        /// <summary>Chuỗi hiển thị trên bảng.</summary>
        public string HienThi =>
            !ThanhCong ? "Không tra được"
                       : (string.IsNullOrWhiteSpace(MoTa) ? (string.IsNullOrEmpty(Ma) ? "?" : Ma) : MoTa);
    }

    /// <summary>Kết quả một lượt tra hàng loạt.</summary>
    public class KetQuaTraNnt
    {
        public List<TinhTrangNnt> Items { get; } = new();
        public int TuCache, GoiMoi, Loi, TruLuot;
        public string ThongBao = "";
    }

    /// <summary>Tra cứu tình trạng NNT qua dịch vụ taxinfo (POST /api/Tracuu/tinhtrangmst).
    ///
    /// Dùng endpoint HÀNG LOẠT: gửi cả danh sách MST trong MỘT request thay vì gọi từng cái —
    /// nhanh hơn nhiều và không nện vào máy chủ như cách cũ (đó là lý do tính năng bị tắt trước đây).
    /// Dịch vụ cache 12 giờ; MST lấy từ cache KHÔNG tốn lượt, MST lỗi được hoàn lượt.</summary>
    public class TaxInfoClient
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private const int CoLo = 100;   // số MST mỗi request

        private readonly TaxInfoSettings _cf;
        public TaxInfoClient(TaxInfoSettings? cf = null) => _cf = cf ?? TaxInfoSettings.HienTai;

        /// <summary>Tra tình trạng cho danh sách MST. Tự GỘP TRÙNG trước khi gửi.
        /// onProgress(đã xong, tổng) để cập nhật tiến độ.</summary>
        public async Task<KetQuaTraNnt> TraAsync(IEnumerable<string> dsMst, Action<int, int>? onProgress = null,
                                                 CancellationToken ct = default)
        {
            var kq = new KetQuaTraNnt();
            if (!_cf.DaCauHinh) { kq.ThongBao = "Chưa cấu hình khóa API tra cứu (vào Cài đặt để nhập)."; return kq; }

            // GỘP TRÙNG: một MST xuất hiện ở nhiều hóa đơn chỉ tra 1 lần.
            var duyNhat = dsMst.Where(m => !string.IsNullOrWhiteSpace(m))
                               .Select(m => m.Trim())
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();
            if (duyNhat.Count == 0) return kq;

            string url = _cf.BaseUrl.TrimEnd('/') + "/api/Tracuu/tinhtrangmst";
            int xong = 0;

            for (int i = 0; i < duyNhat.Count; i += CoLo)
            {
                ct.ThrowIfCancellationRequested();
                var lo = duyNhat.Skip(i).Take(CoLo).ToList();
                try
                {
                    string body = JsonSerializer.Serialize(new { mst = lo, refresh = "none" });
                    using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    req.Headers.TryAddWithoutValidation("X-Api-Key", _cf.ApiKey);
                    req.Headers.TryAddWithoutValidation("X-Api-Secret", _cf.ApiSecret);

                    var resp = await http.SendAsync(req, ct);
                    string json = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        kq.ThongBao = (int)resp.StatusCode == 401 || (int)resp.StatusCode == 403
                            ? "Khóa API tra cứu không hợp lệ hoặc hết hạn."
                            : $"Dịch vụ tra cứu lỗi ({(int)resp.StatusCode}).";
                        return kq;
                    }
                    DocKetQua(json, kq);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TaxInfo] lỗi lô {i}: {ex.Message}");
                    kq.ThongBao = "Không kết nối được dịch vụ tra cứu.";
                }
                xong = Math.Min(duyNhat.Count, i + lo.Count);
                onProgress?.Invoke(xong, duyNhat.Count);
            }
            return kq;
        }

        private static void DocKetQua(string json, KetQuaTraNnt kq)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            kq.TuCache += Int(r, "fromCache");
            kq.GoiMoi += Int(r, "fromGip") + Int(r, "fromHddt");
            kq.Loi += Int(r, "failed");
            kq.TruLuot += Int(r, "consumed");

            if (!r.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
            foreach (var it in items.EnumerateArray())
                kq.Items.Add(new TinhTrangNnt
                {
                    Mst = Str(it, "mst"),
                    ThanhCong = it.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True,
                    Ten = Str(it, "name"),
                    Ma = Str(it, "status"),
                    MoTa = Str(it, "status_text"),
                    ThongBao = Str(it, "message"),
                });
        }

        private static string Str(JsonElement e, string n)
            => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
        private static int Int(JsonElement e, string n)
            => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }
}
