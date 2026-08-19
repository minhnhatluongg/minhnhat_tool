using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    /// <summary>Cào NỀN làm ấm kho dữ liệu: đăng nhập từng công ty đã lưu, cào danh sách hóa đơn
    /// theo ĐÚNG chiều và khoảng ngày người dùng chọn, rồi tải sẵn chi tiết + XML vào
    /// <see cref="HoaDonCache"/>. Chạy CHẬM, KIÊN NHẪN, 1 luồng — không ai đang chờ nên ưu tiên
    /// tuyệt đối việc không làm Tổng cục Thuế chặn. Tờ nào bị chặn thì để lần sau; khi người dùng
    /// xuất thủ công vẫn tự tải đầy đủ, nên cào nền chỉ là bonus tốc độ, không phải nguồn sự thật.
    ///
    /// TÍNH PHÍ: cào nền tiêu hạn mức THEO TỜ (<see cref="QuotaClient"/>). Chỉ xin phép cho những
    /// tờ THỰC SỰ còn thiếu trên máy — tờ đã có sẵn không tốn gì, và mỗi tờ chỉ tính tiền một lần
    /// nên chạy lại ngày hôm sau gần như miễn phí.</summary>
    public static class CaoNenService
    {
        public static bool DangChay { get; private set; }

        /// <summary>Kết quả một lần cào nền — để hiển thị thống kê thay vì chỉ một con số cụt.</summary>
        public class KetQua
        {
            public int SoCty;            // số công ty đã cào xong
            public int SoHoaDon;         // số hóa đơn nằm trong phạm vi
            public int DaCoSan;          // đã có trong kho từ trước -> không gọi TCT, không tốn hạn mức
            public int ChiTietMoi;       // chi tiết tải mới lần này
            public int XmlMoi;           // XML tải mới lần này
            public int ThatBai;          // TCT chặn/không trả -> để lần sau
            public int TinhPhi;          // số tờ bị trừ hạn mức lần này
            public int MienPhi;          // số tờ đã trả tiền từ trước -> lần này không mất gì
            public int HetLuot;          // số tờ không tải được vì hết hạn mức
            public int ConLai = -1;      // hạn mức còn lại sau khi chạy (-1 = không hỏi được)
            public readonly List<string> CanhBao = new();
            public string TomTat = "";
        }

        /// <summary>Làm ấm kho cho tất cả công ty đã lưu mật khẩu. onStatus báo tiến độ (tiếng Việt).</summary>
        public static async Task<KetQua> RunAsync(Action<string>? onStatus, CancellationToken ct)
        {
            var kq = new KetQua();
            if (DangChay) { kq.TomTat = "Cào nền đang chạy."; return kq; }

            var st = CaoNenSettings.HienTai;
            var cacLoai = st.CacLoai();
            if (cacLoai.Length == 0)
            {
                kq.TomTat = "Chưa chọn cào hóa đơn mua vào hay bán ra — không có gì để làm.";
                onStatus?.Invoke(kq.TomTat);
                return kq;
            }

            var quota = new QuotaClient();
            if (!quota.DaCauHinh)
            {
                kq.TomTat = "Chưa nhập khóa API — cào nền tính phí theo tờ hóa đơn nên cần khóa. " +
                            "Vào Cài đặt → Hạn mức để nhập.";
                onStatus?.Invoke(kq.TomTat);
                return kq;
            }

            var (tu, den) = st.KhoangYeuCau();
            if (tu > den)
            {
                kq.TomTat = "Khoảng ngày không hợp lệ.";
                onStatus?.Invoke(kq.TomTat);
                return kq;
            }

            DangChay = true;
            try
            {
                var dsCty = DoanhNghiepStore.Load().Where(c => !string.IsNullOrWhiteSpace(c.Mst)).ToList();
                var coMatKhau = dsCty.Where(c => !string.IsNullOrWhiteSpace(c.Password)).ToList();
                foreach (var bo in dsCty.Where(c => string.IsNullOrWhiteSpace(c.Password)))
                    kq.CanhBao.Add($"Bỏ qua {bo.Mst} — chưa lưu mật khẩu.");

                var client = new HoaDonDienTuClient();
                bool ngungViHetLuot = false;

                foreach (var cty in coMatKhau)
                {
                    if (ngungViHetLuot) break;
                    ct.ThrowIfCancellationRequested();

                    onStatus?.Invoke($"Đăng nhập {cty.Mst}...");
                    string token;
                    try { token = await client.LoginAsync(cty.Mst, cty.Password, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        kq.CanhBao.Add($"Không đăng nhập được {cty.Mst}: {Gon(ex.Message)}");
                        continue;
                    }

                    foreach (var loai in cacLoai)
                    {
                        if (ngungViHetLuot) break;
                        ct.ThrowIfCancellationRequested();
                        string tenChieu = loai == "purchase" ? "mua vào" : "bán ra";

                        onStatus?.Invoke($"{cty.Mst} — lấy danh sách {tenChieu} " +
                                         $"({tu:dd/MM/yyyy} → {den:dd/MM/yyyy})...");
                        var ds = await CaoDanhSachAsync(client, token, loai, tu, den, kq, cty.Mst, ct);
                        if (ds.Count == 0) continue;

                        // Ghi sổ kho ngay: kể cả khi chưa tải chi tiết, vẫn biết kho "đang thiếu gì".
                        HoaDonCache.GhiKho(ds, cty.Mst);
                        kq.SoHoaDon += ds.Count;

                        // Lọc TRƯỚC khi xin hạn mức: chỉ trả tiền cho tờ thực sự còn thiếu trên máy.
                        var canTai = new List<HoaDonInfo>();
                        foreach (var hd in ds)
                        {
                            string k = HoaDonCache.Key(hd.Nbmst, hd.Khhdon, hd.Shdon, hd.Khmshdon);
                            bool thieuCt = !HoaDonCache.TryDetail(k, out _);
                            bool thieuXml = st.LayXml && !HoaDonCache.TryXml(k, out _);
                            if (thieuCt || thieuXml) canTai.Add(hd); else kq.DaCoSan++;
                        }
                        if (canTai.Count == 0) continue;

                        onStatus?.Invoke($"{cty.Mst} — xin hạn mức cho {canTai.Count} tờ {tenChieu}...");
                        var phep = await quota.XinPhepAsync(canTai, ct);
                        kq.TinhPhi += phep.TinhMoi;
                        kq.MienPhi += phep.DaTinhTruoc;
                        kq.HetLuot += phep.TuChoi;
                        if (phep.ConLai >= 0) kq.ConLai = phep.ConLai;
                        if (!string.IsNullOrEmpty(phep.ThongBao))
                            kq.CanhBao.Add($"{cty.Mst} ({tenChieu}): {phep.ThongBao}");

                        // Hết hạn mức hoặc mất liên lạc -> dừng hẳn, đừng quay vòng gọi vô ích.
                        if (phep.TuChoi > 0 || phep.NgoaiTuyen) ngungViHetLuot = true;
                        if (phep.ChoPhep.Count == 0) continue;

                        var lo = phep.ChoPhep;
                        for (int i = 0; i < lo.Count; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var hd = lo[i];
                            string key = HoaDonCache.Key(hd.Nbmst, hd.Khhdon, hd.Shdon, hd.Khmshdon);

                            bool thieuCt = !HoaDonCache.TryDetail(key, out _);
                            bool thieuXml = st.LayXml && !HoaDonCache.TryXml(key, out _);
                            if (!thieuCt && !thieuXml) continue;

                            onStatus?.Invoke($"{cty.Mst} — {tenChieu} {i + 1}/{lo.Count} " +
                                             $"(mới {kq.ChiTietMoi}, có sẵn {kq.DaCoSan})");

                            if (thieuCt)
                            {
                                // Kiên nhẫn vừa phải (3 lượt): chạy nền, không ép Thuế.
                                try
                                {
                                    var (_, tt) = await client.LayChiTietAsync(token, hd, 3, ct);
                                    if (tt == HoaDonDienTuClient.ChiTietTrangThai.ThatBai) kq.ThatBai++;
                                    else kq.ChiTietMoi++;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch { kq.ThatBai++; }
                            }

                            if (thieuXml)
                            {
                                try
                                {
                                    var xml = await client.ExportXmlAsync(token, hd, ct);
                                    if (xml != null && xml.Length > 0) kq.XmlMoi++;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch { }   // TCT không giữ XML gốc của mọi tờ — không tính là lỗi
                            }

                            await Task.Delay(800, ct);   // đi chậm để không bị chặn
                        }
                    }
                    kq.SoCty++;
                }

                kq.TomTat = $"Xong {kq.SoCty} công ty · {kq.SoHoaDon} hóa đơn trong phạm vi · " +
                            $"tải mới {kq.ChiTietMoi} chi tiết, {kq.XmlMoi} XML · có sẵn {kq.DaCoSan}" +
                            (kq.TinhPhi > 0 || kq.MienPhi > 0
                                ? $" · tính phí {kq.TinhPhi} tờ (miễn phí {kq.MienPhi} tờ đã mua)" : "") +
                            (kq.ConLai >= 0 ? $" · còn {kq.ConLai} tờ" : "") +
                            (kq.HetLuot > 0 ? $" · {kq.HetLuot} tờ CHƯA tải được do hết hạn mức" : "") +
                            (kq.ThatBai > 0 ? $" · {kq.ThatBai} tờ để lần sau" : "");

                st.LanChayCuoi = DateTime.Now.ToString("yyyy-MM-dd");
                st.SoHoaDonLanCuoi = kq.ChiTietMoi + kq.XmlMoi;
                st.TomTatLanCuoi = $"{DateTime.Now:dd/MM/yyyy HH:mm} — {kq.TomTat}";
                st.Luu();
                onStatus?.Invoke(kq.TomTat);
            }
            finally { DangChay = false; }
            return kq;
        }

        // Cào danh sách 1 chiều trong khoảng ngày (chia THÁNG vì TCT giới hạn <= 1 tháng/lần)
        private static async Task<List<HoaDonInfo>> CaoDanhSachAsync(
            HoaDonDienTuClient client, string token, string loai, DateTime tu, DateTime den,
            KetQua kq, string mst, CancellationToken ct)
        {
            var all = new List<HoaDonInfo>();
            var s = tu;
            while (s <= den)
            {
                ct.ThrowIfCancellationRequested();
                var e = s.AddMonths(1).AddDays(-1); if (e > den) e = den;
                try
                {
                    all.AddRange(await client.QueryInvoicesAsync(
                        token, loai, s.ToString("dd/MM/yyyy"), e.ToString("dd/MM/yyyy"), null, ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // 1 tháng lỗi -> bỏ qua tháng đó nhưng PHẢI nói ra, đừng im lặng để người dùng
                    // tưởng kho đã đủ.
                    kq.CanhBao.Add($"{mst} — không lấy được danh sách {s:MM/yyyy}: {Gon(ex.Message)}");
                }
                s = e.AddDays(1);
            }
            return all;
        }

        private static string Gon(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > 90 ? s.Substring(0, 90) + "..." : s);
    }
}
