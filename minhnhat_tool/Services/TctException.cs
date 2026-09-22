using System;
using System.Text.Json;

namespace minhnhat_tool.Services
{
    /// <summary>
    /// Lỗi khi gọi Tổng cục Thuế (hoadondientu) hoặc dịch vụ giải captcha.
    ///
    /// Message  = câu ngắn, tiếng Việt, cho người dùng đọc.
    /// ChiTiet  = mã HTTP + phản hồi thô (JSON) / stack trace, để kỹ thuật tra khi cần.
    ///            Hộp thoại lỗi (Ui.LoiDialog) gập phần này lại, KHÔNG in lẫn vào câu chính.
    /// </summary>
    public class TctException : Exception
    {
        /// <summary>Mã HTTP TCT trả về; 0 = không tới được máy chủ (mạng / hết thời gian chờ).</summary>
        public int MaLoi { get; }

        /// <summary>Phần kỹ thuật: "HTTP 403" + JSON thô, hoặc ToString() của lỗi gốc.</summary>
        public string ChiTiet { get; }

        public TctException(string thongBao, int maLoi = 0, string chiTiet = "", Exception? loiGoc = null)
            : base(thongBao, loiGoc)
        {
            MaLoi = maLoi;
            ChiTiet = chiTiet ?? "";
        }

        /// <summary>
        /// Dựng lỗi từ phản hồi HTTP của TCT: dịch mã lỗi + trường "message" trong JSON thành câu dễ hiểu.
        /// <paramref name="viec"/> là việc đang làm, ví dụ "Đăng nhập thất bại", "Tải XML gốc thất bại".
        /// </summary>
        public static TctException TuPhanHoi(string viec, int maLoi, string phanHoi)
        {
            string tctBao = LayMessage(phanHoi);
            string chiTiet = $"HTTP {maLoi}";
            if (!string.IsNullOrWhiteSpace(phanHoi)) chiTiet += Environment.NewLine + phanHoi.Trim();
            return new TctException($"{viec}: {TomTat(maLoi, tctBao)}", maLoi, chiTiet);
        }

        /// <summary>
        /// TCT trả 2xx nhưng thân KHÔNG phải JSON (trang HTML bảo trì / cổng chặn / bị chuyển hướng).
        /// Ghi đủ mã, Content-Type, URL cuối cùng (sau chuyển hướng) và đầu thân phản hồi để chẩn đoán.
        /// </summary>
        public static TctException KhongPhaiJson(string viec, int maLoi, string contentType, string urlCuoi, string than)
        {
            string nl = Environment.NewLine;
            string dau = (than ?? "").Trim();
            if (dau.Length > 2000) dau = dau.Substring(0, 2000) + nl + "... (cắt bớt)";
            string chiTiet = $"HTTP {maLoi} nhưng thân phản hồi KHÔNG phải JSON" + nl +
                             $"Content-Type: {contentType}" + nl +
                             $"URL cuối: {urlCuoi}" + nl + nl + dau;
            return new TctException($"{viec}: Tổng cục Thuế trả về một trang web thay vì dữ liệu " +
                                    "(đang bảo trì hoặc chặn tạm thời). Đợi vài phút rồi thử lại.", maLoi, chiTiet);
        }

        /// <summary>Thân phản hồi có dạng JSON (object/array) không? Dùng để phát hiện TCT trả HTML với mã 200.</summary>
        public static bool LaJson(string? than)
        {
            if (string.IsNullOrWhiteSpace(than)) return false;
            foreach (char c in than)
            {
                if (char.IsWhiteSpace(c) || c == '\uFEFF') continue;
                return c == '{' || c == '[';
            }
            return false;
        }

        /// <summary>Thân phản hồi là trang HTML? (TCT đôi khi trả trang web thay cho XML/PDF.)</summary>
        public static bool LaHtml(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            string dau = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart().ToLowerInvariant();
            return dau.StartsWith("<!doctype html") || dau.StartsWith("<html");
        }

        /// <summary>Câu tóm tắt cho người dùng theo mã HTTP và câu TCT báo (nếu có).</summary>
        private static string TomTat(int ma, string tctBao)
        {
            string b = tctBao.ToLowerInvariant();
            if (b.Contains("captcha") || b.Contains("mã xác nhận"))
                return "mã xác nhận (captcha) giải sai. Hãy bấm thử lại.";
            if (ma == 401 || b.Contains("mật khẩu") || b.Contains("password") || b.Contains("tài khoản"))
                return "sai mã số thuế hoặc mật khẩu.";

            // Câu của TCT (nếu có) đưa xuống dòng riêng, đã là tiếng Việt dễ đọc, không phải JSON.
            string goc = string.IsNullOrEmpty(tctBao) ? "" : Environment.NewLine + "TCT báo: " + tctBao;
            return ma switch
            {
                0      => "không kết nối được tới Tổng cục Thuế (mạng chập chờn hoặc hết thời gian chờ).",
                403    => "Tổng cục Thuế đã chặn yêu cầu (nghi truy cập bất thường). Đợi vài phút rồi thử lại." + goc,
                429    => "Tổng cục Thuế đang giới hạn tần suất truy cập. Đợi vài phút rồi thử lại." + goc,
                >= 500 => "hệ thống Tổng cục Thuế đang lỗi hoặc bảo trì. Thử lại sau." + goc,
                _      => $"Tổng cục Thuế từ chối yêu cầu (mã {ma})." + goc,
            };
        }

        /// <summary>Lấy trường "message" (hoặc "error"/"detail") trong JSON lỗi của TCT; không phải JSON thì trả rỗng.</summary>
        public static string LayMessage(string phanHoi)
        {
            if (string.IsNullOrWhiteSpace(phanHoi)) return "";
            try
            {
                using var doc = JsonDocument.Parse(phanHoi);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return "";
                foreach (var ten in new[] { "message", "error", "detail" })
                    if (root.TryGetProperty(ten, out var m) && m.ValueKind == JsonValueKind.String)
                        return (m.GetString() ?? "").Trim();
            }
            catch (JsonException) { }
            return "";
        }
    }
}
