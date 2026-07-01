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

        /// <summary>Tra tên người nộp thuế theo MST (qua dịch vụ tracuunnt). Trả về (tên, địa chỉ). Rỗng nếu không tìm thấy.</summary>
        public async Task<(string ten, string diaChi)> TcnntLookupAsync(string mst)
        {
            if (string.IsNullOrWhiteSpace(mst)) return ("", "");
            try
            {
                string url = $"{DECAPTCHA}/tcnnt/lookup?mst={Uri.EscapeDataString(mst)}&max_tries=12&delay=1.5&api_key={TCNNT_APIKEY}";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return ("", "");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (!root.TryGetProperty("found", out var f) || !f.GetBoolean()) return ("", "");
                if (root.TryGetProperty("results", out var rs) && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
                {
                    var r0 = rs[0];
                    string ten = r0.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string dc = r0.TryGetProperty("address", out var a) ? (a.GetString() ?? "") : "";
                    return (ten, dc);
                }
            }
            catch { }
            return ("", "");
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

        /// <summary>Cào hóa đơn theo loại: "purchase" = Mua vào (đầu vào), "sold" = Bán ra (đầu ra).</summary>
        public async Task<List<HoaDonInfo>> QueryInvoicesAsync(string token, string loai, string tuNgay, string denNgay)
        {
            string search = $"tdlap=ge={tuNgay}T00:00:00;tdlap=le={denNgay}T23:59:59";
            string url = $"{HDDT}/query/invoices/{loai}?sort=tdlap:desc&size=50&search={Uri.EscapeDataString(search)}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Không tải được hóa đơn ({(int)resp.StatusCode}). {json}");

            var list = new List<HoaDonInfo>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("datas", out var datas) && datas.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in datas.EnumerateArray())
                {
                    list.Add(new HoaDonInfo
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
                    });
                }
            }
            return list;
        }

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
