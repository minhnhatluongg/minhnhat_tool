using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    /// <summary>Hạn mức còn lại của khóa đang dùng.</summary>
    public class HanMuc
    {
        public bool ThanhCong { get; set; }
        public string CongTy { get; set; } = "";
        public string Mst { get; set; } = "";
        public int TongSoTo { get; set; }
        public int DaDung { get; set; }
        public int ConLai { get; set; }
        public bool DangHoatDong { get; set; }
        public string ThongBao { get; set; } = "";
    }

    /// <summary>Kết quả xin phép tải một lô hóa đơn.</summary>
    public class KetQuaXinPhep
    {
        /// <summary>Những tờ được phép tải (đã trả tiền trước đó hoặc vừa trả).</summary>
        public List<HoaDonInfo> ChoPhep { get; } = new();

        public int DaTinhTruoc;    // miễn phí vì đã tính tiền lần trước
        public int TinhMoi;        // vừa bị trừ hạn mức lần này
        public int TuChoi;         // không được tải vì hết hạn mức
        public int ConLai = -1;    // -1 = chưa biết (không hỏi được máy chủ)

        public bool ThanhCong = true;   // liên lạc được máy chủ hạn mức
        public bool NgoaiTuyen;         // mất mạng -> chỉ dùng sổ đã trả tiền trên máy
        public string ThongBao = "";
    }

    /// <summary>
    /// Hạn mức TÍNH THEO TỜ HÓA ĐƠN (POST /api/Quota/hoadon của dịch vụ taxinfo).
    ///
    /// Mỗi tờ chỉ bị tính tiền ĐÚNG MỘT LẦN cho mỗi khóa: máy chủ giữ sổ nên cào lại hôm sau,
    /// cài lại máy hay đổi máy đều không bị tính lại.
    ///
    /// RIÊNG TƯ: chỉ gửi SHA-256 của "apiKey|nbmst|khhdon|shdon|khmshdon" — máy chủ khử trùng
    /// chính xác nhưng không đọc được khách tải hóa đơn của ai. Dữ liệu hóa đơn không rời máy này.
    ///
    /// Hash gắn với apiKey nên ĐỔI KHÓA = mất lịch sử đã trả tiền. Cấp lại khóa cho khách cũ thì
    /// giữ nguyên khóa, đừng tạo khóa mới.
    /// </summary>
    public class QuotaClient
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private const int CoLo = 1000;   // máy chủ nhận tối đa 2000 tờ/lần, đi 1000 cho chắc

        private readonly TaxInfoSettings _cf;
        public QuotaClient(TaxInfoSettings? cf = null) => _cf = cf ?? TaxInfoSettings.HienTai;

        public bool DaCauHinh => _cf.DaCauHinh;

        /// <summary>Định danh một tờ hóa đơn dưới dạng hash — công thức phải khớp giữa các phiên bản,
        /// đổi công thức là toàn bộ khách hàng bị tính tiền lại.</summary>
        public static string Hash(string apiKey, HoaDonInfo hd)
        {
            string tho = $"{apiKey}|{hd.Nbmst}|{hd.Khhdon}|{hd.Shdon}|{hd.Khmshdon}";
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(tho));
            var sb = new StringBuilder(64);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Xem hạn mức còn lại — không tốn lượt.</summary>
        public async Task<HanMuc> ThongTinAsync(CancellationToken ct = default)
        {
            if (!_cf.DaCauHinh)
                return new HanMuc { ThongBao = "Chưa nhập khóa API (vào Cài đặt để nhập)." };
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"{_cf.BaseUrl.TrimEnd('/')}/api/Quota/thongtin");
                Ky(req);
                var resp = await http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return new HanMuc { ThongBao = MoTaLoi((int)resp.StatusCode, body) };

                var d = JsonSerializer.Deserialize<QuotaInfoDto>(body, JsonOpt);
                if (d == null) return new HanMuc { ThongBao = "Máy chủ trả dữ liệu không đọc được." };
                return new HanMuc
                {
                    ThanhCong = true,
                    CongTy = d.congTy ?? "",
                    Mst = d.mst ?? "",
                    TongSoTo = d.tongSoTo,
                    DaDung = d.daDung,
                    ConLai = d.conLai,
                    DangHoatDong = d.dangHoatDong
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new HanMuc { ThongBao = "Không gọi được dịch vụ hạn mức: " + ex.Message }; }
        }

        /// <summary>
        /// Xin phép tải một lô hóa đơn. Trả về đúng những tờ được phép.
        ///
        /// Tờ đã ghi trong sổ đã-trả-tiền trên máy thì KHÔNG hỏi lại máy chủ — vừa nhanh vừa để
        /// mất mạng vẫn tải tiếp được phần đã mua.
        /// </summary>
        public async Task<KetQuaXinPhep> XinPhepAsync(IList<HoaDonInfo> ds, CancellationToken ct = default)
        {
            var kq = new KetQuaXinPhep();
            if (ds == null || ds.Count == 0) return kq;

            if (!_cf.DaCauHinh)
            {
                kq.ThanhCong = false;
                kq.TuChoi = ds.Count;
                kq.ThongBao = "Chưa nhập khóa API — cào nền cần hạn mức theo tờ hóa đơn. Vào Cài đặt để nhập khóa.";
                return kq;
            }

            // 1 hash <-> nhiều tờ là chuyện bình thường (cùng tờ xuất hiện ở 2 công ty trên máy).
            var theoHash = new Dictionary<string, List<HoaDonInfo>>(StringComparer.Ordinal);
            foreach (var hd in ds)
            {
                if (hd == null) continue;
                string h = Hash(_cf.ApiKey, hd);
                if (!theoHash.TryGetValue(h, out var l)) theoHash[h] = l = new List<HoaDonInfo>();
                l.Add(hd);
            }

            var daTra = HoaDonCache.LocDaTra(theoHash.Keys);
            var canHoi = theoHash.Keys.Where(h => !daTra.Contains(h)).ToList();

            foreach (var h in daTra) kq.ChoPhep.AddRange(theoHash[h]);
            kq.DaTinhTruoc = daTra.Count;

            if (canHoi.Count == 0) return kq;   // tất cả đã trả tiền -> khỏi gọi mạng

            var moiDuocCap = new List<string>();
            for (int i = 0; i < canHoi.Count; i += CoLo)
            {
                ct.ThrowIfCancellationRequested();
                var lo = canHoi.GetRange(i, Math.Min(CoLo, canHoi.Count - i));
                var r = await GoiAsync(lo, ct);

                if (!r.ok)
                {
                    // Mất mạng/máy chủ lỗi: KHÔNG tự cho tải phần chưa trả tiền, nhưng phần đã mua
                    // vẫn dùng được bình thường.
                    kq.ThanhCong = false;
                    kq.NgoaiTuyen = true;
                    kq.TuChoi += canHoi.Count - i;
                    kq.ThongBao = r.thongBao;
                    break;
                }

                kq.DaTinhTruoc += r.daTinhTruoc;
                kq.TinhMoi += r.tinhMoi;
                kq.TuChoi += r.tuChoi;
                kq.ConLai = r.conLai;
                if (!string.IsNullOrEmpty(r.thongBao)) kq.ThongBao = r.thongBao;

                foreach (var h in r.choPhep)
                    if (theoHash.TryGetValue(h, out var l)) { kq.ChoPhep.AddRange(l); moiDuocCap.Add(h); }

                if (r.tuChoi > 0) break;   // hết hạn mức -> xin tiếp cũng vô ích
            }

            if (moiDuocCap.Count > 0) HoaDonCache.GhiDaTra(moiDuocCap);
            return kq;
        }

        // ===== gọi 1 lô =====
        private async Task<(bool ok, int daTinhTruoc, int tinhMoi, int tuChoi, int conLai,
                            List<string> choPhep, string thongBao)> GoiAsync(List<string> lo, CancellationToken ct)
        {
            var rong = new List<string>();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{_cf.BaseUrl.TrimEnd('/')}/api/Quota/hoadon");
                Ky(req);
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new { hoaDon = lo, uocLuong = false }),
                    Encoding.UTF8, "application/json");

                var resp = await http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return (false, 0, 0, 0, -1, rong, MoTaLoi((int)resp.StatusCode, body));

                var d = JsonSerializer.Deserialize<ClaimDto>(body, JsonOpt);
                if (d == null) return (false, 0, 0, 0, -1, rong, "Máy chủ trả dữ liệu không đọc được.");
                if (!d.success) return (false, 0, 0, 0, d.conLai, rong, d.message ?? "Máy chủ từ chối.");

                return (true, d.daTinhTruoc, d.tinhMoi, d.tuChoi, d.conLai, d.choPhep ?? rong, d.message ?? "");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return (false, 0, 0, 0, -1, rong, "Không gọi được dịch vụ hạn mức: " + ex.Message);
            }
        }

        private void Ky(HttpRequestMessage req)
        {
            req.Headers.Add("X-Api-Key", _cf.ApiKey);
            req.Headers.Add("X-Api-Secret", _cf.ApiSecret);
        }

        private static string MoTaLoi(int code, string body)
        {
            if (body != null && body.Contains("OUT_OF_USES")) return "Hết hạn mức — vui lòng nạp thêm số tờ.";
            if (body != null && body.Contains("API_KEY_DEACTIVE")) return "Khóa API đang bị khóa.";
            return code switch
            {
                401 => "Khóa API không hợp lệ hoặc hết hạn.",
                404 => "Máy chủ chưa có API hạn mức (/api/Quota) — cần cập nhật bản backend mới.",
                429 => "Hết hạn mức — vui lòng nạp thêm số tờ.",
                _ => $"Dịch vụ hạn mức lỗi ({code})."
            };
        }

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private class ClaimDto
        {
            public bool success { get; set; } = true;
            public string? message { get; set; }
            public int tong { get; set; }
            public int daTinhTruoc { get; set; }
            public int tinhMoi { get; set; }
            public int tuChoi { get; set; }
            public int conLai { get; set; }
            public List<string>? choPhep { get; set; }
        }

        private class QuotaInfoDto
        {
            public bool success { get; set; }
            public string? congTy { get; set; }
            public string? mst { get; set; }
            public int tongSoTo { get; set; }
            public int daDung { get; set; }
            public int conLai { get; set; }
            public bool dangHoatDong { get; set; }
        }
    }
}
