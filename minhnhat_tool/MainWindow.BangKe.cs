using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ClosedXML.Excel;
using minhnhat_tool.Models;

namespace minhnhat_tool
{
    // ===== Xuất BẢNG KÊ khai thuế GTGT theo MẪU MISA (mẫu quản trị) =====
    // MISA liệt kê theo TỪNG DÒNG HÀNG, có cột "Mặt hàng" + "Tài khoản thuế",
    // nhóm theo thuế suất, tổng đặt trên dòng nhóm.
    public partial class MainWindow
    {
        private class LineKe
        {
            public string SoHD = "", Ngay = "", Ten = "", Mst = "", MatHang = "", SuatText = "";
            public decimal GiaTri, Thue;
            public int Bucket;   // 0=KCT,1=0%,2=5%,3=8%,4=10%,5=khác
        }

        private async void mnuBangKe_Click(object sender, RoutedEventArgs e)
        {
            var rows = _hoaDon.ToList();   // theo đúng phần đang lọc/hiển thị
            if (rows.Count == 0) { MessageBox.Show("Chưa có hóa đơn để lập bảng kê."); return; }
            if (string.IsNullOrEmpty(_token))
            { MessageBox.Show("Bấm 'Đồng bộ' trước để đăng nhập — bảng kê MISA cần chi tiết từng dòng hàng."); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = (_lastIsMuaVao ? "BangKeMISA_MuaVao_" : "BangKeMISA_BanRa_") + $"{Session.Mst}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            ShowProgress($"Đang lập bảng kê {rows.Count} hóa đơn...");
            try
            {
                var lines = new List<LineKe>();
                int i = 0;
                foreach (var row in rows)
                {
                    if (Cancelled) break;
                    i++;
                    SetProgress(i, rows.Count, $"Đang lấy chi tiết {i}/{rows.Count} hóa đơn...");
                    var hd = row.Raw!;
                    string dj = "";
                    for (int a = 0; a < 3 && string.IsNullOrEmpty(dj); a++)
                    {
                        if (a > 0) await Task.Delay(500 * a);
                        try { dj = await _tct.GetInvoiceDetailAsync(_token, hd); } catch { dj = ""; }
                    }
                    ParseLines(dj, hd, row.NgayLap, _lastIsMuaVao, lines);
                    await Task.Delay(150);
                }

                using var wb = new XLWorkbook();
                BuildMisa(wb, lines, _lastIsMuaVao);
                wb.SaveAs(dlg.FileName);
                MessageBox.Show($"Đã xuất bảng kê MISA ({lines.Count} dòng hàng / {rows.Count} hóa đơn):\n{dlg.FileName}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Lỗi xuất bảng kê: " + ex.Message); }
            finally { HideProgress(); }
        }

        // Tách từng dòng hàng của 1 hóa đơn thành LineKe
        private static void ParseLines(string dj, HoaDonInfo hd, string ngay, bool muaVao, List<LineKe> outp)
        {
            string ten = muaVao ? hd.Nbten : hd.Nmten;
            string mst = muaVao ? hd.Nbmst : hd.Nmmst;

            if (!string.IsNullOrEmpty(dj))
            {
                try
                {
                    var r = JsonDocument.Parse(dj).RootElement;
                    if (r.TryGetProperty("hdhhdvu", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            decimal thtien = (decimal)ExD(it, "thtien");
                            decimal tsuat = (decimal)ExD(it, "tsuat");
                            decimal tthue = (decimal)ExD(it, "tthue");
                            if (tthue <= 0) tthue = Math.Round(thtien * tsuat, 0);
                            string lt = ExS(it, "ltsuat");
                            string st = SuatText(lt, tsuat);
                            outp.Add(new LineKe
                            {
                                SoHD = hd.Shdon, Ngay = ngay, Ten = ten, Mst = mst,
                                MatHang = ExS(it, "ten"), GiaTri = thtien, Thue = tthue,
                                SuatText = st, Bucket = BucketOf(st)
                            });
                        }
                        return;
                    }
                }
                catch { }
            }

            // Không lấy được chi tiết -> 1 dòng ở mức hóa đơn (không mất hóa đơn)
            string st0 = SuatHoaDon(hd);
            outp.Add(new LineKe
            {
                SoHD = hd.Shdon, Ngay = ngay, Ten = ten, Mst = mst,
                MatHang = "(chưa lấy được chi tiết)", GiaTri = hd.Tgtcthue, Thue = hd.Tgtthue,
                SuatText = st0, Bucket = BucketOf(st0)
            });
        }

        private static string SuatText(string ltsuat, decimal tsuat)
        {
            string s = (ltsuat ?? "").Trim().ToLowerInvariant();
            if (s.Contains("kct") || s.Contains("không chịu")) return "KCT";
            int pct = (int)Math.Round(tsuat * 100);
            if (pct > 0) return $"{pct} %";
            return s.Contains("0") ? "0 %" : "KCT";   // tsuat=0: có ghi 0% -> 0%, còn lại coi là KCT
        }

        private static string SuatHoaDon(HoaDonInfo hd)
        {
            if (hd.Tgtcthue <= 0) return "KCT";
            int pct = (int)Math.Round(hd.Tgtthue / hd.Tgtcthue * 100m);
            int[] mocs = { 0, 5, 8, 10 }; int best = 10; decimal bd = decimal.MaxValue;
            foreach (var m in mocs) { var d = Math.Abs(pct - m); if (d < bd) { bd = d; best = m; } }
            return best == 0 ? (hd.Tgtthue == 0 ? "KCT" : "0 %") : $"{best} %";
        }

        private static int BucketOf(string suatText) => suatText switch
        {
            "KCT" => 0, "0 %" => 1, "5 %" => 2, "8 %" => 3, "10 %" => 4, _ => 5
        };

        private static readonly string[] _tenNhom =
        {
            "1. Hàng hóa, dịch vụ không chịu thuế GTGT",
            "2. Hàng hóa, dịch vụ chịu thuế suất GTGT 0%",
            "3. Hàng hóa, dịch vụ chịu thuế suất GTGT 5%",
            "4. Hàng hóa, dịch vụ chịu thuế suất GTGT 8%",
            "5. Hàng hóa, dịch vụ chịu thuế suất GTGT 10%",
            "6. Hàng hóa, dịch vụ khác",
        };

        private void BuildMisa(XLWorkbook wb, List<LineKe> lines, bool muaVao)
        {
            var ws = wb.AddWorksheet(muaVao ? "Mua vào" : "Bán ra");
            const int nCol = 9;
            string doiTac = muaVao ? "người bán" : "người mua";
            string giaTriHeader = muaVao ? "Giá trị HHDV mua vào chưa có thuế GTGT" : "Doanh số bán chưa có thuế GTGT";
            string tkThue = muaVao ? "1331" : "33311";

            ws.Cell(1, 1).Value = "BẢNG KÊ HÓA ĐƠN, CHỨNG TỪ HÀNG HÓA, DỊCH VỤ " + (muaVao ? "MUA VÀO" : "BÁN RA");
            Merge(ws, 1, nCol, true, 14, XLAlignmentHorizontalValues.Center);
            ws.Cell(2, 1).Value = $"Kỳ tính thuế: {KyTinhThue()}";
            Merge(ws, 2, nCol, false, 11, XLAlignmentHorizontalValues.Center);
            ws.Cell(3, 1).Value = $"Tên người nộp thuế: {Session.TenDN}     Mã số thuế: {Session.Mst}";
            Merge(ws, 3, nCol, false, 11, XLAlignmentHorizontalValues.Left);

            int hr = 4;
            string[] h = { "Số hóa đơn", "Ngày hóa đơn", $"Tên {doiTac}", $"Mã số thuế {doiTac}",
                           "Mặt hàng", giaTriHeader, "Thuế suất", "Thuế GTGT", "Tài khoản thuế" };
            for (int c = 0; c < nCol; c++) ws.Cell(hr, c + 1).Value = h[c];

            int r = hr + 1;
            decimal gCt = 0, gThue = 0;

            // Mua vào: MISA gộp 1 nhóm. Bán ra: tách theo thuế suất.
            var buckets = muaVao ? new[] { -1 } : new[] { 0, 1, 2, 3, 4, 5 };
            foreach (var b in buckets)
            {
                var grp = (b == -1 ? lines : lines.Where(x => x.Bucket == b)).ToList();
                if (grp.Count == 0) continue;

                decimal sCt = grp.Sum(x => x.GiaTri), sThue = grp.Sum(x => x.Thue);
                string tenNhom = muaVao
                    ? "Nhóm HHDV mua vào: Hàng hóa, dịch vụ dùng cho SXKD chịu thuế GTGT đủ điều kiện khấu trừ"
                    : "Nhóm HHDV: " + _tenNhom[b];
                ws.Cell(r, 1).Value = tenNhom;
                ws.Cell(r, 6).Value = sCt;
                ws.Cell(r, 8).Value = sThue;
                ws.Range(r, 1, r, nCol).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F1"));
                r++;

                foreach (var l in grp)
                {
                    ws.Cell(r, 1).Value = l.SoHD;
                    ws.Cell(r, 2).Value = l.Ngay;
                    ws.Cell(r, 3).Value = l.Ten;
                    ws.Cell(r, 4).Value = l.Mst;
                    ws.Cell(r, 5).Value = l.MatHang;
                    ws.Cell(r, 6).Value = l.GiaTri;
                    ws.Cell(r, 7).Value = l.SuatText;
                    ws.Cell(r, 8).Value = l.Thue;
                    ws.Cell(r, 9).Value = tkThue;
                    r++;
                }
                gCt += sCt; gThue += sThue;
            }

            ws.Cell(r, 5).Value = "TỔNG CỘNG";
            ws.Cell(r, 6).Value = gCt;
            ws.Cell(r, 8).Value = gThue;
            ws.Range(r, 1, r, nCol).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#FFD200"));

            // Định dạng
            var head = ws.Range(hr, 1, hr, nCol);
            head.Style.Font.Bold = true;
            head.Style.Alignment.WrapText = true;
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#B8CCE4");
            var body = ws.Range(hr, 1, r, nCol);
            body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(hr + 1, 6, r, 6).Style.NumberFormat.Format = "#,##0";
            ws.Range(hr + 1, 8, r, 8).Style.NumberFormat.Format = "#,##0";
            ws.SheetView.FreezeRows(hr);
            ws.Columns().AdjustToContents();
            ws.Column(5).Width = Math.Min(ws.Column(5).Width, 40);
        }

        private string KyTinhThue()
        {
            string tu = dpTuNgay.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
            string den = dpDenNgay.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
            return $"từ ngày {tu} đến ngày {den}";
        }

        private static void Merge(IXLWorksheet ws, int row, int nCol, bool bold, double size, XLAlignmentHorizontalValues al)
        {
            var rg = ws.Range(row, 1, row, nCol); rg.Merge();
            rg.Style.Font.Bold = bold; rg.Style.Font.FontSize = size;
            rg.Style.Alignment.Horizontal = al;
        }
    }
}
