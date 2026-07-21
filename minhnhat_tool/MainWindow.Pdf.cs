using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using minhnhat_tool.Models;
using minhnhat_tool.Services;

namespace minhnhat_tool
{
    // ===== Xuất hóa đơn theo LÔ ra thư mục riêng =====
    // Hai lệnh TÁCH BIỆT:
    //   • Xuất PDF  -> bản thể hiện để xem/in nhanh (dựng từ dữ liệu gốc TCT, phủ 100% kể cả HĐ máy tính tiền)
    //   • Xuất XML  -> bản GỐC có chữ ký số (giá trị pháp lý theo NĐ 123/2020)
    // Mỗi lệnh tự tạo 1 thư mục riêng cho cả lô.
    public partial class MainWindow
    {
        // ---------- PDF ----------
        private async void mnuPdfChon_Click(object sender, RoutedEventArgs e)
        {
            var sel = ChonDong(); if (sel == null) return;
            await XuatPdfAsync(sel);
        }

        private async void mnuPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_hoaDon.Count == 0) { MessageBox.Show("Chưa có hóa đơn để xuất."); return; }
            await XuatPdfAsync(_hoaDon.ToList());
        }

        // ---------- XML ----------
        private async void mnuXmlChon_Click(object sender, RoutedEventArgs e)
        {
            var sel = ChonDong(); if (sel == null) return;
            await XuatXmlAsync(sel);
        }

        private async void mnuXml_Click(object sender, RoutedEventArgs e)
        {
            if (_hoaDon.Count == 0) { MessageBox.Show("Chưa có hóa đơn để xuất."); return; }
            await XuatXmlAsync(_hoaDon.ToList());
        }

        private List<InvoiceRow>? ChonDong()
        {
            var sel = grdHoaDon.SelectedItems.Cast<InvoiceRow>().ToList();
            if (sel.Count == 0)
            { MessageBox.Show("Bôi đen các dòng cần xuất trước.\nGiữ Ctrl để chọn rời, giữ Shift để chọn 1 dải."); return null; }
            return sel;
        }

        // ================= XUẤT PDF (chỉ PDF) =================
        private async Task XuatPdfAsync(List<InvoiceRow> rows)
        {
            string? thuMuc = TaoThuMucLo("PDF", rows.Count);
            if (thuMuc == null) return;

            ShowProgress($"Đang xuất {rows.Count} file PDF...");
            var log = MoLog("PDF (bản thể hiện để xem/in)", rows.Count);
            int ok = 0, loi = 0;
            var daDung = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var pdf = new PdfRenderer();
            try
            {
                // (1) Lấy CHI TIẾT cho bằng đủ trước (tuần tự, lặp nhiều lượt — ưu tiên không sót tờ nào)
                var djs = await LayChiTietDayDuAsync(rows);

                // (2) Dựng PDF tuần tự (WebView2 phải chạy trên UI thread)
                await pdf.InitAsync();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (Cancelled) break;
                    var row = rows[i]; var hd = row.Raw!;
                    SetProgress(i + 1, rows.Count, $"Đang dựng PDF {i + 1}/{rows.Count}...");
                    string ten = TenFile(hd, daDung);
                    string canhBao = "";
                    try
                    {
                        string html = HoaDonViewer.BuildHtml(hd, Session.TenDN, _lastIsMuaVao, djs[i]);
                        if (await pdf.HtmlToPdfAsync(html, Path.Combine(thuMuc, ten + ".pdf"))) ok++;
                        else { canhBao = "không dựng được PDF"; loi++; }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { canhBao = "lỗi: " + ex.Message; loi++; }

                    if (canhBao.Length == 0 && string.IsNullOrEmpty(djs[i]))
                        canhBao = "thiếu chi tiết (TCT chặn) — PDF chỉ có thông tin tổng hợp";

                    log.AppendLine($"[{i + 1}/{rows.Count}] {row.LoaiHD,-13} SốHĐ:{hd.Shdon,-10} -> {ten}.pdf" +
                                   (canhBao.Length > 0 ? "   ⚠ " + canhBao : ""));
                }

                KetThuc(log, thuMuc, $"{ok} file PDF", loi);
                MessageBox.Show($"Xong: {ok} file PDF{(loi > 0 ? $"  ({loi} tờ có cảnh báo — xem log.txt)" : "")}" +
                                $"{(Cancelled ? "\n(Đã hủy giữa chừng)" : "")}\n\n{thuMuc}",
                                "Xuất PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                MoThuMuc(thuMuc);
            }
            catch (OperationCanceledException) { HuyGiuaChung(log, thuMuc, $"{ok} PDF"); }
            catch (Exception ex) { MessageBox.Show("Lỗi xuất PDF: " + ex.Message); }
            finally { HideProgress(); }
        }

        // ================= XUẤT XML GỐC (chỉ XML) =================
        private async Task XuatXmlAsync(List<InvoiceRow> rows)
        {
            string? thuMuc = TaoThuMucLo("XML", rows.Count);
            if (thuMuc == null) return;

            ShowProgress($"Đang tải {rows.Count} file XML gốc...");
            var log = MoLog("XML GỐC có chữ ký số (giá trị pháp lý)", rows.Count);
            int ok = 0, loi = 0, done = 0;
            var daDung = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dong = new string[rows.Count];

            try
            {
                // Đặt tên trước (tuần tự) để tránh tranh chấp khi chạy song song
                var tens = new string[rows.Count];
                for (int k = 0; k < rows.Count; k++) tens[k] = TenFile(rows[k].Raw!, daDung);

                // Tải XML SONG SONG — mỗi hóa đơn chỉ 1 lượt gọi, không cần chi tiết
                using var sem = new System.Threading.SemaphoreSlim(4);
                var tasks = new List<Task>();
                for (int k = 0; k < rows.Count; k++)
                {
                    int idx = k;
                    tasks.Add(Task.Run(async () =>
                    {
                        await sem.WaitAsync(Ct);
                        try
                        {
                            var hd = rows[idx].Raw!;
                            try
                            {
                                var bytes = await _tct.ExportXmlAsync(_token, hd, Ct);
                                bool isZip = bytes.Length > 1 && bytes[0] == 0x50 && bytes[1] == 0x4B;   // "PK" = zip
                                await File.WriteAllBytesAsync(Path.Combine(thuMuc, tens[idx] + (isZip ? ".zip" : ".xml")), bytes, Ct);
                                System.Threading.Interlocked.Increment(ref ok);
                                string ma = TimMaTraCuuTuXml(XmlTuBytes(bytes));
                                dong[idx] = $"[{idx + 1}/{rows.Count}] {rows[idx].LoaiHD,-13} SốHĐ:{hd.Shdon,-10} -> {tens[idx]}" +
                                            (isZip ? ".zip" : ".xml") + (ma.Length > 0 ? $"   [mã tra cứu: {ma}]" : "");
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                System.Threading.Interlocked.Increment(ref loi);
                                dong[idx] = $"[{idx + 1}/{rows.Count}] SốHĐ:{hd.Shdon,-10} ⚠ KHÔNG tải được XML: {ex.Message}";
                            }
                        }
                        finally
                        {
                            sem.Release();
                            int n = System.Threading.Interlocked.Increment(ref done);
                            Dispatcher.Invoke(() => SetProgress(n, rows.Count, $"Đang tải XML {n}/{rows.Count}..."));
                        }
                    }, Ct));
                }
                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) { }   // hủy -> vẫn ghi phần đã tải

                foreach (var d in dong) if (!string.IsNullOrEmpty(d)) log.AppendLine(d);

                KetThuc(log, thuMuc, $"{ok} file XML gốc", loi);
                MessageBox.Show($"Xong: {ok} file XML gốc{(loi > 0 ? $"  ({loi} tờ lỗi — xem log.txt)" : "")}" +
                                $"{(Cancelled ? "\n(Đã hủy giữa chừng)" : "")}\n\n{thuMuc}",
                                "Xuất XML gốc", MessageBoxButton.OK, MessageBoxImage.Information);
                MoThuMuc(thuMuc);
            }
            catch (OperationCanceledException) { HuyGiuaChung(log, thuMuc, $"{ok} XML"); }
            catch (Exception ex) { MessageBox.Show("Lỗi xuất XML: " + ex.Message); }
            finally { HideProgress(); }
        }

        // ================= Dùng chung =================

        /// <summary>Lấy chi tiết TẤT CẢ hóa đơn với mục tiêu ĐỦ 10/10, không bỏ sót tờ nào.
        /// Chạy TUẦN TỰ 1 luồng (song song chính là thứ làm TCT chặn tới 60% số tờ).
        /// Lặp NHIỀU LƯỢT, mỗi lượt chậm hơn lượt trước, chỉ đòi lại những tờ CHƯA có kết quả cuối cùng.
        /// Vẫn còn sót thì HỎI người dùng chứ không im lặng xuất thiếu.</summary>
        private async Task<string[]> LayChiTietDayDuAsync(List<InvoiceRow> rows)
        {
            var djs = new string[rows.Count];
            var xong = new bool[rows.Count];   // true = đã có KẾT QUẢ CUỐI (có chi tiết, hoặc chắc chắn không có)
            for (int k = 0; k < rows.Count; k++) djs[k] = "";

            // Mỗi lượt: (giãn cách giữa các tờ, số lần thử mỗi tờ). Lượt sau kiên nhẫn hơn lượt trước.
            var luots = new (int giaCach, int soLanThu)[] { (250, 4), (1200, 5), (3000, 6), (6000, 6) };

            for (int L = 0; L < luots.Length; L++)
            {
                var canLam = new List<int>();
                for (int k = 0; k < rows.Count; k++) if (!xong[k]) canLam.Add(k);
                if (canLam.Count == 0 || Cancelled) break;

                string nhan = L == 0 ? "Đang lấy chi tiết"
                                     : $"Lượt {L + 1}: đòi lại {canLam.Count} tờ còn thiếu (đi chậm để TCT không chặn)";
                for (int j = 0; j < canLam.Count; j++)
                {
                    if (Cancelled) return djs;
                    int idx = canLam[j];
                    SetProgress(j + 1, canLam.Count, $"{nhan} {j + 1}/{canLam.Count}...");
                    try
                    {
                        var (json, tt) = await _tct.LayChiTietAsync(_token, rows[idx].Raw!, luots[L].soLanThu, Ct);
                        if (tt != Services.HoaDonDienTuClient.ChiTietTrangThai.ThatBai)
                        {
                            djs[idx] = json;
                            xong[idx] = true;      // ThanhCong hoặc KhongCo -> kết quả cuối, thôi đòi
                        }
                    }
                    catch (OperationCanceledException) { return djs; }
                    catch { }
                    if (j < canLam.Count - 1) await Task.Delay(luots[L].giaCach, Ct);
                }
            }

            // Vẫn còn tờ chưa có kết quả cuối -> KHÔNG im lặng, hỏi người dùng
            var soT = new List<int>();
            for (int k = 0; k < rows.Count; k++) if (!xong[k]) soT.Add(k);
            if (soT.Count > 0 && !Cancelled)
            {
                string ds = string.Join(", ", soT.Take(10).Select(k => rows[k].Raw!.Shdon));
                if (soT.Count > 10) ds += $"... (+{soT.Count - 10} tờ)";
                var tl = MessageBox.Show(
                    $"Còn {soT.Count}/{rows.Count} hóa đơn CHƯA lấy được chi tiết do Tổng cục Thuế chặn:\n{ds}\n\n" +
                    "• CÓ  = thử lại thêm một lượt nữa (rất chậm, chắc ăn hơn)\n" +
                    "• KHÔNG = vẫn xuất, các tờ này chỉ có thông tin tổng hợp",
                    "Chưa lấy đủ chi tiết", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (tl == MessageBoxResult.Yes)
                {
                    for (int j = 0; j < soT.Count; j++)
                    {
                        if (Cancelled) break;
                        int idx = soT[j];
                        SetProgress(j + 1, soT.Count, $"Thử lại lần cuối {j + 1}/{soT.Count}...");
                        try
                        {
                            var (json, tt) = await _tct.LayChiTietAsync(_token, rows[idx].Raw!, 8, Ct);
                            if (tt != Services.HoaDonDienTuClient.ChiTietTrangThai.ThatBai) djs[idx] = json;
                        }
                        catch (OperationCanceledException) { break; } catch { }
                        if (j < soT.Count - 1) await Task.Delay(8000, Ct);
                    }
                }
            }
            return djs;
        }

        /// <summary>Chọn thư mục cha rồi TẠO thư mục riêng cho cả lô. Trả về null nếu người dùng hủy.</summary>
        private string? TaoThuMucLo(string loai, int soTo)
        {
            if (string.IsNullOrEmpty(_token))
            { MessageBox.Show("Bấm 'Đồng bộ' trước để đăng nhập — cần dữ liệu gốc từ Tổng cục Thuế."); return null; }

            var pick = new Microsoft.Win32.OpenFolderDialog
            { Title = $"Chọn nơi lưu — sẽ tạo thư mục {loai} riêng cho lô {soTo} hóa đơn này" };
            if (pick.ShowDialog() != true) return null;

            string chieu = _lastIsMuaVao ? "MuaVao" : "BanRa";
            string thuMuc = Path.Combine(pick.FolderName,
                Sach($"{loai}_{Session.Mst}_{chieu}_{soTo}to_{DateTime.Now:yyyyMMdd_HHmm}"));
            try { Directory.CreateDirectory(thuMuc); return thuMuc; }
            catch (Exception ex) { MessageBox.Show("Không tạo được thư mục:\n" + ex.Message); return null; }
        }

        private StringBuilder MoLog(string loai, int soTo)
        {
            var log = new StringBuilder();
            log.AppendLine($"Lô {soTo} hóa đơn {(_lastIsMuaVao ? "MUA VÀO" : "BÁN RA")} — {Session.TenDN} ({Session.Mst})");
            log.AppendLine($"Nội dung: {loai}");
            log.AppendLine($"Xuất lúc {DateTime.Now:dd/MM/yyyy HH:mm}");
            log.AppendLine(new string('-', 100));
            return log;
        }

        private void KetThuc(StringBuilder log, string thuMuc, string ketQua, int loi)
        {
            log.AppendLine(new string('-', 100));
            log.AppendLine($"Kết quả: {ketQua}" + (loi > 0 ? $", {loi} tờ có cảnh báo" : "") +
                           (Cancelled ? " (ĐÃ HỦY giữa chừng)" : ""));
            GhiLog(log, thuMuc);
        }

        private static void GhiLog(StringBuilder log, string thuMuc)
        {
            try { File.WriteAllText(Path.Combine(thuMuc, "log.txt"), log.ToString(), new UTF8Encoding(true)); } catch { }
        }

        private void HuyGiuaChung(StringBuilder log, string thuMuc, string daCo)
        {
            GhiLog(log, thuMuc);
            MessageBox.Show($"Đã hủy. Giữ lại {daCo} đã xuất:\n{thuMuc}",
                            "Đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void MoThuMuc(string thuMuc)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(thuMuc) { UseShellExecute = true }); } catch { }
        }

        // Đặt tên file: {SốHĐ}_{ddMM}_{MST đối tác} — trùng thì thêm hậu tố _2, _3...
        private string TenFile(HoaDonInfo hd, HashSet<string> daDung)
        {
            string ngay = DateTime.TryParse(hd.Tdlap, out var d) ? d.ToString("ddMM") : "";
            string mst = _lastIsMuaVao ? hd.Nbmst : hd.Nmmst;
            string ten = Sach($"{hd.Shdon}_{ngay}_{mst}");
            string thu = ten;
            for (int n = 2; !daDung.Add(thu); n++) thu = $"{ten}_{n}";
            return thu;
        }

        private static string Sach(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
