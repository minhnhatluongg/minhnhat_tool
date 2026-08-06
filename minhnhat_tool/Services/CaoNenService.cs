using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    /// <summary>Cào NỀN làm ấm cache: đăng nhập từng công ty đã lưu, cào danh sách trong cửa sổ ngày,
    /// rồi tải sẵn chi tiết + XML vào <see cref="HoaDonCache"/>. Chạy CHẬM, KIÊN NHẪN, 1 luồng —
    /// không ai chờ nên ưu tiên tuyệt đối việc không làm TCT chặn. Tờ nào bị chặn thì bỏ qua để lần
    /// sau; khi người dùng xuất thủ công vẫn tự tải đầy đủ. Cache nền chỉ là bonus tốc độ.</summary>
    public static class CaoNenService
    {
        public static bool DangChay { get; private set; }

        /// <summary>Làm ấm cache cho tất cả công ty đã lưu (có mật khẩu). onStatus báo tiến độ (chuỗi tiếng Việt).</summary>
        public static async Task<int> RunAsync(Action<string>? onStatus, CancellationToken ct)
        {
            if (DangChay) return 0;
            DangChay = true;
            int tong = 0;
            try
            {
                var dsCty = DoanhNghiepStore.Load();
                var client = new HoaDonDienTuClient();
                foreach (var cty in dsCty)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(cty.Mst) || string.IsNullOrWhiteSpace(cty.Password))
                        continue;   // chưa lưu mật khẩu -> bỏ qua công ty này

                    onStatus?.Invoke($"Cào nền: đăng nhập {cty.Mst}...");
                    string token;
                    try { token = await client.LoginAsync(cty.Mst, cty.Password, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch { continue; }   // đăng nhập lỗi (sai mật khẩu/TCT từ chối) -> bỏ qua

                    var den = DateTime.Today;
                    var tu = den.AddDays(-Math.Max(1, CaoNenSettings.HienTai.SoNgay));

                    foreach (var loai in new[] { "purchase", "sold" })
                    {
                        var ds = await CaoDanhSachAsync(client, token, loai, tu, den, ct);
                        for (int i = 0; i < ds.Count; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var hd = ds[i];
                            onStatus?.Invoke($"Cào nền {cty.Mst} ({(loai == "purchase" ? "vào" : "ra")}): " +
                                             $"{i + 1}/{ds.Count} — đã lưu {tong}");
                            // Đi qua cache: chỉ tải cái CHƯA có. Kiên nhẫn vừa phải (3 lần) — nền, không ép.
                            try { await client.LayChiTietAsync(token, hd, 3, ct); }
                            catch (OperationCanceledException) { throw; } catch { }
                            try { await client.ExportXmlAsync(token, hd, ct); }
                            catch (OperationCanceledException) { throw; } catch { }
                            tong++;
                            await Task.Delay(800, ct);   // đi chậm để không bị chặn
                        }
                    }
                }

                var st = CaoNenSettings.HienTai;
                st.LanChayCuoi = DateTime.Now.ToString("yyyy-MM-dd");
                st.SoHoaDonLanCuoi = tong;
                st.Luu();
                onStatus?.Invoke($"Cào nền xong: đã làm ấm {tong} hóa đơn.");
            }
            finally { DangChay = false; }
            return tong;
        }

        // Cào danh sách 1 chiều trong khoảng ngày (chia THÁNG vì TCT giới hạn <= 1 tháng/lần)
        private static async Task<List<HoaDonInfo>> CaoDanhSachAsync(
            HoaDonDienTuClient client, string token, string loai, DateTime tu, DateTime den, CancellationToken ct)
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
                catch { }   // 1 tháng lỗi -> bỏ qua tháng đó, vẫn làm ấm các tháng khác
                s = e.AddDays(1);
            }
            return all;
        }
    }
}
