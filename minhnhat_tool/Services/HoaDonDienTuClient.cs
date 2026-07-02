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
        public async Task<List<HoaDonInfo>> QueryInvoicesAsync(string token, string loai, string tuNgay, string denNgay)
        {
            var list = new List<HoaDonInfo>();
            // 1) Hóa đơn điện tử thường
            list.AddRange(await QueryPagedAsync(token, "query", loai, tuNgay, denNgay, required: true));
            // 2) Hóa đơn có mã khởi tạo từ máy tính tiền (POS) — best-effort, không có thì bỏ qua
            list.AddRange(await QueryPagedAsync(token, "sco-query", loai, tuNgay, denNgay, required: false));

            // Khử trùng lặp theo (MST bán + ký hiệu + số + mẫu số)
            var seen = new HashSet<string>();
            var result = new List<HoaDonInfo>();
            foreach (var x in list)
                if (seen.Add($"{x.Nbmst}|{x.Khhdon}|{x.Shdon}|{x.Khmshdon}"))
                    result.Add(x);
            return result;
        }

        // Tải 1 nguồn, tự PHÂN TRANG theo con trỏ "state" (TCT trả tối đa 50 dòng/lần)
        private async Task<List<HoaDonInfo>> QueryPagedAsync(string token, string prefix, string loai, string tuNgay, string denNgay, bool required)
        {
            var list = new List<HoaDonInfo>();
            string search = $"tdlap=ge={tuNgay}T00:00:00;tdlap=le={denNgay}T23:59:59";
            string? state = null;
            int guard = 0;
            do
            {
                string url = $"{HDDT}/{prefix}/invoices/{loai}?sort=tdlap:desc&size=50&search={Uri.EscapeDataString(search)}";
                if (!string.IsNullOrEmpty(state)) url += $"&state={Uri.EscapeDataString(state)}";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Authorization", "Bearer " + token);
                var resp = await http.SendAsync(req);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    if (!required) return list;   // endpoint POS có thể không dùng được -> bỏ qua
                    throw new Exception($"Không tải được hóa đơn ({(int)resp.StatusCode}). {json}");
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("datas", out var datas) && datas.ValueKind == JsonValueKind.Array)
                    foreach (var it in datas.EnumerateArray())
                        list.Add(ParseHoaDon(it));

                state = root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
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

        /// <summary>Lấy CHI TIẾT 1 hóa đơn (gồm các dòng hàng hóa) -> JSON. Best-effort.</summary>
        public async Task<string> GetInvoiceDetailAsync(string token, HoaDonInfo hd)
        {
            string url = $"{HDDT}/query/invoices/detail" +
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
            string url = $"{HDDT}/query/invoices/{kind}" +
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
            string url = $"{HDDT}/query/invoices/export-xml" +
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
            string url = $"{HDDT}/query/invoices/export-pdf" +
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
