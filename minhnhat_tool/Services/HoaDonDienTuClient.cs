using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    public class HoaDonDienTuClient
    {
        private static readonly HttpClient http = new HttpClient();

        private const string DECAPTCHA = "https://decapcha.win-tech.vn";
        private const string HDDT = "https://hoadondientu.gdt.gov.vn/api";
        // API key cho dịch vụ tra cứu MST (tracuunnt) — dùng để lấy TÊN nhà cung cấp từ MST
        private const string TCNNT_APIKEY = "dk_Fyl_NHVTteBK1m436yjbvCDBEmuJxWmr";

        /// <summary>Tra người nộp thuế theo MST (qua dịch vụ tracuunnt). Trả về (tên, địa chỉ, tình trạng). Rỗng nếu không tìm thấy.</summary>
        public async Task<(string ten, string diaChi, string status)> TcnntLookupAsync(string mst)
        {
            if (string.IsNullOrWhiteSpace(mst)) return ("", "", "");
            try
            {
                string url = $"{DECAPTCHA}/tcnnt/lookup?mst={Uri.EscapeDataString(mst)}&max_tries=12&delay=1.5&api_key={TCNNT_APIKEY}";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return ("", "", "");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (!root.TryGetProperty("found", out var f) || !f.GetBoolean()) return ("", "", "");
                if (root.TryGetProperty("results", out var rs) && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
                {
                    var r0 = rs[0];
                    string ten = r0.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string dc = r0.TryGetProperty("address", out var a) ? (a.GetString() ?? "") : "";
                    string st = r0.TryGetProperty("status", out var s) ? (s.GetString() ?? "") : "";
                    return (ten, dc, st);
                }
            }
            catch { }
            return ("", "", "");
        }

        /// <summary>Đăng nhập hoadondientu (vượt captcha tự động) -> Bearer token.</summary>
        public async Task<string> LoginAsync(string username, string password)
        {
            var cap = await http.GetFromJsonAsync<CaptchaSolve>($"{DECAPTCHA}/captcha/solve");
            if (cap == null || string.IsNullOrEmpty(cap.token))
                throw new Exception("Không kết nối được dịch vụ giải mã xác nhận.");

            var body = new
            {
                ckey = cap.key,
                cvalue = cap.token,
                username = username,
                password = password
            };

            var resp = await http.PostAsJsonAsync($"{HDDT}/security-taxpayer/authenticate", body);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception("Sai MST hoặc mật khẩu (hoặc TCT từ chối). Chi tiết: " + json);

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString() ?? "";
        }

        /// <summary>Cào hóa đơn theo loại: "purchase" = Mua vào (đầu vào), "sold" = Bán ra (đầu ra).
        /// Gộp CẢ HAI nguồn: hóa đơn điện tử thường (/query) và hóa đơn có mã khởi tạo từ máy tính tiền (/sco-query).</summary>
        public async Task<List<HoaDonInfo>> QueryInvoicesAsync(string token, string loai, string tuNgay, string denNgay,
                                                               Action<int>? onProgress = null)
        {
            var list = new List<HoaDonInfo>();
            // 1) Hóa đơn điện tử thường
            list.AddRange(await QueryPagedAsync(token, "query", loai, tuNgay, denNgay, required: true, onProgress, baseCount: 0));
            // 2) Hóa đơn có mã khởi tạo từ máy tính tiền (POS) — best-effort, không có thì bỏ qua
            list.AddRange(await QueryPagedAsync(token, "sco-query", loai, tuNgay, denNgay, required: false, onProgress, baseCount: list.Count));

            // Khử trùng lặp theo (MST bán + ký hiệu + số + mẫu số)
            var seen = new HashSet<string>();
            var result = new List<HoaDonInfo>();
            foreach (var x in list)
                if (seen.Add($"{x.Nbmst}|{x.Khhdon}|{x.Shdon}|{x.Khmshdon}"))
                    result.Add(x);
            return result;
        }

        // Tải 1 nguồn, tự PHÂN TRANG theo con trỏ "state" (TCT trả tối đa 50 dòng/lần).
        // Có THỬ LẠI khi TCT chặn tạm thời (429/5xx) để KHÔNG trả về THIẾU hóa đơn.
        // onProgress: báo tổng số HĐ đã tải (baseCount + số của nguồn này) sau mỗi trang.
        private async Task<List<HoaDonInfo>> QueryPagedAsync(string token, string prefix, string loai, string tuNgay,
                                                             string denNgay, bool required, Action<int>? onProgress, int baseCount)
        {
            var list = new List<HoaDonInfo>();
            var seenPage = new HashSet<string>();   // chống lặp vô hạn khi con trỏ state không tiến
            string search = $"tdlap=ge={tuNgay}T00:00:00;tdlap=le={denNgay}T23:59:59";
            string? state = null;
            int guard = 0;
            bool firstPage = true;
            do
            {
                string url = $"{HDDT}/{prefix}/invoices/{loai}?sort=tdlap:desc&size=50&search={Uri.EscapeDataString(search)}";
                if (!string.IsNullOrEmpty(state)) url += $"&state={Uri.EscapeDataString(state)}";

                // Thử lại tối đa 5 lần (giãn dần) vì TCT hay chặn khi phân trang liên tục
                HttpResponseMessage? resp = null;
                string json = "";
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (attempt > 0) await Task.Delay(700 * attempt);
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("Authorization", "Bearer " + token);
                    resp = await http.SendAsync(req);
                    json = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode) break;
                }

                if (resp == null || !resp.IsSuccessStatusCode)
                {
                    int code = resp == null ? 0 : (int)resp.StatusCode;
                    bool transient = code == 0 || code == 429 || code >= 500;   // chặn tạm thời / lỗi mạng
                    // Trang ĐẦU nguồn POS lỗi KHÔNG do chặn (vd 400/404: tài khoản không có nguồn này) -> bỏ qua êm.
                    if (!required && firstPage && !transient) return list;
                    // Còn lại (đang phân trang dở, hoặc bị chặn tạm thời): KHÔNG im lặng cắt bớt (sẽ THIẾU hóa đơn) -> báo lỗi để đồng bộ lại.
                    throw new Exception($"Không tải hết hóa đơn ({prefix}/{loai}) — TCT chặn ({code}). Hãy bấm Đồng bộ lại. {json}");
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                bool pos = prefix == "sco-query";   // hóa đơn khởi tạo từ máy tính tiền
                int newCount = 0;
                if (root.TryGetProperty("datas", out var datas) && datas.ValueKind == JsonValueKind.Array)
                    foreach (var it in datas.EnumerateArray())
                    {
                        var hd = ParseHoaDon(it);
                        hd.MayTinhTien = pos;
                        if (seenPage.Add($"{hd.Nbmst}|{hd.Khhdon}|{hd.Shdon}|{hd.Khmshdon}"))
                        {
                            list.Add(hd);
                            newCount++;
                        }
                    }

                onProgress?.Invoke(baseCount + list.Count);   // cập nhật tiến độ sau mỗi trang
                state = root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                firstPage = false;
                if (newCount == 0) break;   // trang không thêm HĐ mới -> hết dữ liệu (chống lặp vô hạn)
                if (!string.IsNullOrEmpty(state)) await Task.Delay(200);   // giãn nhẹ giữa các trang để đỡ bị chặn
            }
            while (!string.IsNullOrEmpty(state) && ++guard < 200);
            return list;
        }

        private static HoaDonInfo ParseHoaDon(JsonElement it) => new HoaDonInfo
        {
            Khmshdon = GetStr(it, "khmshdon"),
            Khhdon   = GetStr(it, "khhdon"),
            Shdon    = GetStr(it, "shdon"),
            Tdlap    = GetStr(it, "tdlap"),
            Nbmst    = GetStr(it, "nbmst"),
            Nbten    = GetStr(it, "nbten"),
            Nmmst    = GetStr(it, "nmmst"),
            Nmten    = GetStr(it, "nmten"),
            Tgtcthue = GetDec(it, "tgtcthue"),
            Tgtthue  = GetDec(it, "tgtthue"),
            Tgtttbso = GetDec(it, "tgtttbso"),
            Ttcktmai = GetDec(it, "ttcktmai"),
            Ttxly    = GetInt(it, "ttxly"),
            Tthai    = GetInt(it, "tthai"),
            Nmdchi   = GetStr(it, "nmdchi"),
            Dvtte    = GetStr(it, "dvtte"),
            Tgia     = GetDec(it, "tgia"),
            Tgtphi   = GetDec(it, "tgtphi"),
        };

        // HĐ máy tính tiền (POS) dùng nhánh /sco-query cho MỌI thao tác chi tiết; HĐ điện tử thường dùng /query.
        // Dùng sai nhánh -> TCT trả rỗng nên hóa đơn POS bị thiếu chi tiết/XML khi xuất Excel.
        private static string ApiPrefix(HoaDonInfo hd) => hd.MayTinhTien ? "sco-query" : "query";

        /// <summary>Lấy CHI TIẾT 1 hóa đơn (gồm các dòng hàng hóa) -> JSON. Best-effort.</summary>
        public async Task<string> GetInvoiceDetailAsync(string token, HoaDonInfo hd)
        {
            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/detail" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khhdon={Uri.EscapeDataString(hd.Khhdon)}" +
                         $"&shdon={Uri.EscapeDataString(hd.Shdon)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : "";
        }

        /// <summary>Hóa đơn liên quan (relative) -> JSON array. Rỗng nếu không có.</summary>
        public Task<string> GetRelativeAsync(string token, HoaDonInfo hd)
            => GetInvoicesSubAsync(token, hd, "relative");

        /// <summary>Thông tin liên quan (related) -> JSON. Rỗng nếu không có.</summary>
        public Task<string> GetRelatedAsync(string token, HoaDonInfo hd)
            => GetInvoicesSubAsync(token, hd, "related");

        private async Task<string> GetInvoicesSubAsync(string token, HoaDonInfo hd, string kind)
        {
            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/{kind}" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}" +
                         $"&khhdon={Uri.EscapeDataString(hd.Khhdon)}&shdon={Uri.EscapeDataString(hd.Shdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : "";
        }

        /// <summary>Tải XML gốc (có chữ ký số) của 1 hóa đơn -> bytes.</summary>
        public async Task<byte[]> ExportXmlAsync(string token, HoaDonInfo hd)
        {
            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/export-xml" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khhdon={Uri.EscapeDataString(hd.Khhdon)}" +
                         $"&shdon={Uri.EscapeDataString(hd.Shdon)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Tải XML lỗi ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}");
            return await resp.Content.ReadAsByteArrayAsync();
        }

        /// <summary>Tải PDF gốc của 1 hóa đơn -> bytes (endpoint đoán: export-pdf).</summary>
        public async Task<byte[]> ExportPdfAsync(string token, HoaDonInfo hd)
        {
            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/export-pdf" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khhdon={Uri.EscapeDataString(hd.Khhdon)}" +
                         $"&shdon={Uri.EscapeDataString(hd.Shdon)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Tải PDF lỗi ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}");
            return await resp.Content.ReadAsByteArrayAsync();
        }

        // Đọc giá trị dạng chuỗi: hỗ trợ cả String lẫn Number (shdon/khmshdon là number)
        private static string GetStr(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return "";
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "")
                 : v.ValueKind == JsonValueKind.Number ? v.ToString()
                 : "";
        }
        private static decimal GetDec(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
        private static int GetInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }
}
