using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    public class HoaDonDienTuClient
    {
        // Mọi request tới hoadondientu đi qua handler bên dưới -> tự gắn Request-Id, không sót endpoint nào.
        private static readonly HttpClient http = new HttpClient(new HddtHeaderHandler());

        private const string DECAPTCHA = "https://decapcha.win-tech.vn";
        private const string HDDT = "https://hoadondientu.gdt.gov.vn/api";

        /// <summary>
        /// Từ 9/2026 TCT bắt buộc header "Request-Id" (UUID v4, MỚI cho mỗi request) trên mọi lời gọi
        /// tới hoadondientu.gdt.gov.vn; thiếu là bị 403 "Hệ thống phát hiện hành vi không hợp lệ".
        /// Gắn ở tầng handler để đăng nhập, phân trang, chi tiết, XML, PDF... đều có mà không phải sửa từng chỗ.
        /// </summary>
        private sealed class HddtHeaderHandler : DelegatingHandler
        {
            public HddtHeaderHandler() : base(new HttpClientHandler()) { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                if (req.RequestUri != null &&
                    req.RequestUri.Host.EndsWith("hoadondientu.gdt.gov.vn", StringComparison.OrdinalIgnoreCase))
                {
                    req.Headers.Remove("Request-Id");
                    req.Headers.TryAddWithoutValidation("Request-Id", Guid.NewGuid().ToString());
                }
                return base.SendAsync(req, ct);
            }
        }
        // API key cho dịch vụ tra cứu MST (tracuunnt) — dùng để lấy TÊN nhà cung cấp từ MST
        private const string TCNNT_APIKEY = "dk_Fyl_NHVTteBK1m436yjbvCDBEmuJxWmr";

        /// <summary>Tra người nộp thuế theo MST (qua dịch vụ tracuunnt). Trả về (tên, địa chỉ, tình trạng). Rỗng nếu không tìm thấy.</summary>
        public async Task<(string ten, string diaChi, string status)> TcnntLookupAsync(string mst, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(mst)) return ("", "", "");
            try
            {
                string url = $"{DECAPTCHA}/tcnnt/lookup?mst={Uri.EscapeDataString(mst)}&max_tries=12&delay=1.5&api_key={TCNNT_APIKEY}";
                var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return ("", "", "");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
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
            catch (OperationCanceledException) { throw; }   // Hủy phải nổi lên, không được nuốt
            catch { }
            return ("", "", "");
        }

        /// <summary>Đăng nhập hoadondientu (vượt captcha tự động) -> Bearer token.
        /// Mọi thất bại ném TctException: Message là câu dễ hiểu, phản hồi thô nằm ở ChiTiet.</summary>
        public async Task<string> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            // 1) Nhờ dịch vụ decapcha lấy + giải captcha của TCT
            CaptchaSolve? cap;
            try
            {
                cap = await http.GetFromJsonAsync<CaptchaSolve>($"{DECAPTCHA}/captcha/solve", ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)   // lỗi mạng / hết thời gian chờ / dịch vụ trả lỗi
            {
                throw new TctException("Đăng nhập thất bại: không kết nối được dịch vụ giải mã xác nhận (captcha). " +
                                       "Kiểm tra mạng rồi thử lại.", 0, ex.ToString(), ex);
            }
            if (cap == null || string.IsNullOrEmpty(cap.token))
                throw new TctException("Đăng nhập thất bại: dịch vụ giải mã xác nhận (captcha) không trả kết quả. Thử lại sau ít giây.",
                                       0, cap == null ? "captcha/solve trả về rỗng" : JsonSerializer.Serialize(cap));

            // 2) Đăng nhập TCT bằng captcha đã giải
            var body = new
            {
                ckey = cap.key,
                cvalue = cap.token,
                username = username,
                password = password
            };

            HttpResponseMessage resp;
            string json;
            try
            {
                resp = await http.PostAsJsonAsync($"{HDDT}/security-taxpayer/authenticate", body, ct);
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                throw new TctException("Đăng nhập thất bại: không kết nối được tới Tổng cục Thuế (mạng chập chờn hoặc hết thời gian chờ).",
                                       0, ex.ToString(), ex);
            }
            if (!resp.IsSuccessStatusCode)
                throw TctException.TuPhanHoi("Đăng nhập thất bại", (int)resp.StatusCode, json);

            // 3) Lấy token; TCT trả 200 mà không có token cũng coi là lỗi, không để nổ KeyNotFound
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("token", out var tk) &&
                    tk.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(tk.GetString()))
                    return tk.GetString()!;
            }
            catch (JsonException) { }
            throw new TctException("Đăng nhập thất bại: Tổng cục Thuế không trả về mã phiên đăng nhập.",
                                   (int)resp.StatusCode, $"HTTP {(int)resp.StatusCode}" + Environment.NewLine + json);
        }

        /// <summary>Cào hóa đơn theo loại: "purchase" = Mua vào (đầu vào), "sold" = Bán ra (đầu ra).
        /// Gộp CẢ HAI nguồn: hóa đơn điện tử thường (/query) và hóa đơn có mã khởi tạo từ máy tính tiền (/sco-query).</summary>
        public async Task<List<HoaDonInfo>> QueryInvoicesAsync(string token, string loai, string tuNgay, string denNgay,
                                                               Action<string, int>? onProgress = null, CancellationToken ct = default)
        {
            var list = new List<HoaDonInfo>();
            // 1) Hóa đơn điện tử thường
            list.AddRange(await QueryPagedAsync(token, "query", loai, tuNgay, denNgay, required: true, onProgress, baseCount: 0, ct));
            // 2) Hóa đơn có mã khởi tạo từ máy tính tiền (POS) — best-effort, không có thì bỏ qua
            list.AddRange(await QueryPagedAsync(token, "sco-query", loai, tuNgay, denNgay, required: false, onProgress, baseCount: list.Count, ct));

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
                                                             string denNgay, bool required, Action<string, int>? onProgress, int baseCount,
                                                             CancellationToken ct = default)
        {
            string nguon = prefix == "sco-query" ? "hóa đơn máy tính tiền" : "hóa đơn thường";
            var list = new List<HoaDonInfo>();
            var seenPage = new HashSet<string>();   // chống lặp vô hạn khi con trỏ state không tiến
            string search = $"tdlap=ge={tuNgay}T00:00:00;tdlap=le={denNgay}T23:59:59";
            string? state = null;
            int guard = 0, khongThem = 0;   // khongThem: số trang liên tiếp KHÔNG thêm hóa đơn mới
            bool firstPage = true;
            do
            {
                ct.ThrowIfCancellationRequested();   // bấm Hủy -> dừng ngay, không chờ hết trang
                string url = $"{HDDT}/{prefix}/invoices/{loai}?sort=tdlap:desc&size=50&search={Uri.EscapeDataString(search)}";
                if (!string.IsNullOrEmpty(state)) url += $"&state={Uri.EscapeDataString(state)}";

                // Thử lại tối đa 5 lần (giãn dần) vì TCT hay chặn khi phân trang liên tục.
                // Mỗi lần có TIMEOUT RIÊNG 30s -> request treo không kéo dài 100s (mặc định) làm "đứng".
                HttpResponseMessage? resp = null;
                string json = "";
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (attempt > 0) await Task.Delay(700 * attempt, ct);
                    try
                    {
                        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        reqCts.CancelAfter(TimeSpan.FromSeconds(30));
                        var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Add("Authorization", "Bearer " + token);
                        resp = await http.SendAsync(req, reqCts.Token);
                        json = await resp.Content.ReadAsStringAsync(reqCts.Token);
                        // TCT thỉnh thoảng trả 200 kèm TRANG HTML (bảo trì / cổng chặn) thay vì JSON
                        // -> coi như 1 lần trượt, thử lại; tuyệt đối không đem HTML đi parse.
                        if (resp.IsSuccessStatusCode && TctException.LaJson(json)) break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }  // Hủy thật -> nổi lên
                    catch (OperationCanceledException) { resp = null; }   // quá 30s -> coi như 1 lần trượt, thử lại
                    catch (HttpRequestException) { resp = null; }         // lỗi mạng -> thử lại
                }

                bool khongPhaiJson = resp != null && resp.IsSuccessStatusCode && !TctException.LaJson(json);
                if (resp == null || !resp.IsSuccessStatusCode || khongPhaiJson)
                {
                    int code = resp == null ? 0 : (int)resp.StatusCode;
                    bool transient = code == 0 || code == 429 || code >= 500 || khongPhaiJson;   // chặn tạm thời / lỗi mạng / timeout / trang HTML
                    // Trang ĐẦU nguồn POS lỗi KHÔNG do chặn (vd 400/404: tài khoản không có nguồn này) -> bỏ qua êm.
                    if (!required && firstPage && !transient) return list;
                    // Còn lại (đang phân trang dở, hoặc bị chặn tạm thời): KHÔNG im lặng cắt bớt (sẽ THIẾU hóa đơn) -> báo lỗi để đồng bộ lại.
                    if (khongPhaiJson)
                        throw TctException.KhongPhaiJson($"Không tải hết {nguon}, hãy bấm Đồng bộ lại", code,
                                                         resp!.Content.Headers.ContentType?.ToString() ?? "(không có)",
                                                         resp.RequestMessage?.RequestUri?.ToString() ?? url, json);
                    throw TctException.TuPhanHoi($"Không tải hết {nguon}, hãy bấm Đồng bộ lại", code, json);
                }

                int truoc = list.Count;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                bool pos = prefix == "sco-query";   // hóa đơn khởi tạo từ máy tính tiền
                if (root.TryGetProperty("datas", out var datas) && datas.ValueKind == JsonValueKind.Array)
                    foreach (var it in datas.EnumerateArray())
                    {
                        var hd = ParseHoaDon(it);
                        hd.MayTinhTien = pos;
                        // Dedup phòng con trỏ trả trùng ở ranh giới trang — KHÔNG làm mất HĐ thật.
                        if (seenPage.Add($"{hd.Nbmst}|{hd.Khhdon}|{hd.Shdon}|{hd.Khmshdon}"))
                            list.Add(hd);
                    }

                onProgress?.Invoke(nguon, baseCount + list.Count);   // cập nhật tiến độ sau mỗi trang
                string? prevState = state;   // con trỏ vừa dùng cho trang này
                state = root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                firstPage = false;
                // Dừng nếu con trỏ KHÔNG tiến (lặp thật)...
                if (!string.IsNullOrEmpty(state) && state == prevState) break;
                // ...hoặc 2 trang LIÊN TIẾP không thêm hóa đơn mới (con trỏ vẫn tiến nhưng dữ liệu trùng)
                // -> tránh "đứng ở N" khi TCT trả trang lặp cho nguồn máy tính tiền.
                if (list.Count == truoc) { if (++khongThem >= 2) break; }
                else khongThem = 0;
                if (!string.IsNullOrEmpty(state)) await Task.Delay(200, ct);   // giãn nhẹ giữa các trang để đỡ bị chặn
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
        public async Task<string> GetInvoiceDetailAsync(string token, HoaDonInfo hd, CancellationToken ct = default)
            => (await LayChiTietAsync(token, hd, 5, ct)).json;

        /// <summary>Kết quả lấy chi tiết — PHẢI phân biệt được để không im lặng bỏ sót hóa đơn.</summary>
        public enum ChiTietTrangThai
        {
            ThanhCong,   // có dữ liệu chi tiết
            KhongCo,     // TCT khẳng định hóa đơn này KHÔNG có chi tiết (kết quả cuối, không cần đòi lại)
            ThatBai      // bị chặn / lỗi mạng -> CHƯA biết, PHẢI thử lại
        }

        /// <summary>Lấy chi tiết 1 hóa đơn, báo rõ trạng thái để tầng trên biết tờ nào cần đòi lại.</summary>
        public async Task<(string json, ChiTietTrangThai tt)> LayChiTietAsync(
            string token, HoaDonInfo hd, int soLanThu, CancellationToken ct = default)
        {
            // CACHE trước: hóa đơn bất biến -> khỏi tải lại. json rỗng đã cache = "không có chi tiết".
            string key = HoaDonCache.Key(hd.Nbmst, hd.Khhdon, hd.Shdon, hd.Khmshdon);
            if (HoaDonCache.TryDetail(key, out var cached))
                return (cached, cached.Length == 0 ? ChiTietTrangThai.KhongCo : ChiTietTrangThai.ThanhCong);

            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/detail" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khhdon={Uri.EscapeDataString(hd.Khhdon)}" +
                         $"&shdon={Uri.EscapeDataString(hd.Shdon)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}";

            int cho = 600;
            for (int a = 0; a < Math.Max(1, soLanThu); a++)
            {
                if (a > 0) { await Task.Delay(cho, ct); cho = Math.Min(cho * 2, 15000); }   // giãn gấp đôi, trần 15s
                try
                {
                    using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    reqCts.CancelAfter(TimeSpan.FromSeconds(30));
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("Authorization", "Bearer " + token);
                    var resp = await http.SendAsync(req, reqCts.Token);

                    if (resp.IsSuccessStatusCode)
                    {
                        string s = await resp.Content.ReadAsStringAsync(reqCts.Token);
                        // 200 nhưng thân là HTML (bảo trì / chặn) -> KHÔNG được cache rác, coi như trượt và thử lại
                        if (!string.IsNullOrWhiteSpace(s) && !TctException.LaJson(s)) continue;
                        // 200 + rỗng = TCT khẳng định không có chi tiết -> kết quả CUỐI CÙNG
                        bool coCt = !string.IsNullOrWhiteSpace(s);
                        HoaDonCache.PutDetail(key, coCt ? s : "");   // cache kết quả CHẮC CHẮN (kể cả "không có")
                        return coCt ? (s, ChiTietTrangThai.ThanhCong) : ("", ChiTietTrangThai.KhongCo);
                    }
                    // 404 = không tồn tại chi tiết -> kết quả cuối. Mọi mã khác (400/401/403/429/5xx)
                    // đều coi là BỊ CHẶN và phải thử lại — thà chậm còn hơn bỏ sót dữ liệu.
                    if ((int)resp.StatusCode == 404) { HoaDonCache.PutDetail(key, ""); return ("", ChiTietTrangThai.KhongCo); }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { }   // quá 30s -> thử lại
                catch (HttpRequestException) { }         // lỗi mạng -> thử lại
            }
            return ("", ChiTietTrangThai.ThatBai);
        }

        /// <summary>Hóa đơn liên quan (relative) -> JSON array. Rỗng nếu không có.</summary>
        public Task<string> GetRelativeAsync(string token, HoaDonInfo hd, CancellationToken ct = default)
            => GetInvoicesSubAsync(token, hd, "relative", ct);

        /// <summary>Thông tin liên quan (related) -> JSON. Rỗng nếu không có.</summary>
        public Task<string> GetRelatedAsync(string token, HoaDonInfo hd, CancellationToken ct = default)
            => GetInvoicesSubAsync(token, hd, "related", ct);

        private async Task<string> GetInvoicesSubAsync(string token, HoaDonInfo hd, string kind, CancellationToken ct = default)
        {
            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/{kind}" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}" +
                         $"&khhdon={Uri.EscapeDataString(hd.Khhdon)}&shdon={Uri.EscapeDataString(hd.Shdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return "";
            string s = await resp.Content.ReadAsStringAsync(ct);
            return TctException.LaJson(s) ? s : "";   // 200 kèm HTML -> coi như không có
        }

        /// <summary>Tải XML gốc (có chữ ký số) của 1 hóa đơn -> bytes. Có cache bền (XML bất biến).</summary>
        public async Task<byte[]> ExportXmlAsync(string token, HoaDonInfo hd, CancellationToken ct = default)
        {
            string key = HoaDonCache.Key(hd.Nbmst, hd.Khhdon, hd.Shdon, hd.Khmshdon);
            if (HoaDonCache.TryXml(key, out var cached)) return cached;

            string url = $"{HDDT}/{ApiPrefix(hd)}/invoices/export-xml" +
                         $"?nbmst={Uri.EscapeDataString(hd.Nbmst)}&khhdon={Uri.EscapeDataString(hd.Khhdon)}" +
                         $"&shdon={Uri.EscapeDataString(hd.Shdon)}&khmshdon={Uri.EscapeDataString(hd.Khmshdon)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", "Bearer " + token);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                throw TctException.TuPhanHoi("Tải XML gốc thất bại", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (TctException.LaHtml(bytes))   // 200 nhưng là trang web -> không phải XML, không được cache
                throw TctException.KhongPhaiJson("Tải XML gốc thất bại", (int)resp.StatusCode,
                                                 resp.Content.Headers.ContentType?.ToString() ?? "(không có)",
                                                 resp.RequestMessage?.RequestUri?.ToString() ?? url,
                                                 System.Text.Encoding.UTF8.GetString(bytes));
            HoaDonCache.PutXml(key, bytes);   // chỉ cache khi tải THÀNH CÔNG
            return bytes;
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
                throw TctException.TuPhanHoi("Tải PDF thất bại", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
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
