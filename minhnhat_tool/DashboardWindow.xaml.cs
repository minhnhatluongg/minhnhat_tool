using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using minhnhat_tool.Models;

namespace minhnhat_tool
{
    public partial class DashboardWindow : Window
    {
        public DashboardWindow(List<InvoiceRow> rows, bool isMuaVao)
        {
            InitializeComponent();

            var data = rows.Where(r => r.Raw != null).Select(r => r.Raw!).ToList();

            // KPI
            decimal chua = data.Sum(x => x.Tgtcthue), thue = data.Sum(x => x.Tgtthue), tt = data.Sum(x => x.Tgtttbso);
            kpiSoHD.Text = data.Count.ToString("N0");
            kpiChuaThue.Text = chua.ToString("N0");
            kpiThue.Text = thue.ToString("N0");
            kpiThanhToan.Text = tt.ToString("N0");

            VeBieuDoThang(data);
            VeTopDoiTac(data, isMuaVao);
        }

        // ===== Biểu đồ cột theo tháng (chưa thuế + thuế, stacked) =====
        private void VeBieuDoThang(List<HoaDonInfo> data)
        {
            var theoThang = data
                .Select(x => new { d = ParseNgay(x.Tdlap), x.Tgtcthue, x.Tgtthue })
                .Where(a => a.d != null)
                .GroupBy(a => new { a.d!.Value.Year, a.d.Value.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Chua = g.Sum(a => a.Tgtcthue),
                    Thue = g.Sum(a => a.Tgtthue)
                })
                .OrderBy(a => a.Year).ThenBy(a => a.Month)
                .ToList();

            if (theoThang.Count == 0) return;
            decimal max = theoThang.Max(a => a.Chua + a.Thue);
            if (max <= 0) max = 1;
            const double chartH = 200;

            foreach (var m in theoThang)
            {
                double hChua = (double)(m.Chua / max) * (chartH - 10);
                double hThue = (double)(m.Thue / max) * (chartH - 10);

                var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Width = 34 };
                bars.Children.Add(new Border { Height = hThue, Background = Brush("#34d399") });                          // thuế (trên)
                bars.Children.Add(new Border { Height = hChua, Background = Brush("#38bdf8"), CornerRadius = new CornerRadius(0, 0, 0, 0) }); // chưa thuế (dưới)

                var host = new Grid { Height = chartH };
                host.Children.Add(bars);

                var col = new StackPanel { Width = 54, Margin = new Thickness(3, 0, 3, 0) };
                col.Children.Add(host);
                col.Children.Add(new TextBlock
                {
                    Text = $"{m.Month:00}/{m.Year % 100:00}",
                    Foreground = Brush("#94a3b8"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                col.ToolTip = $"Tháng {m.Month:00}/{m.Year}\nChưa thuế: {m.Chua:N0}\nThuế: {m.Thue:N0}\nThanh toán: {(m.Chua + m.Thue):N0}";
                pnlThang.Children.Add(col);
            }
        }

        // ===== Top 10 đối tác theo tổng thanh toán =====
        private void VeTopDoiTac(List<HoaDonInfo> data, bool isMuaVao)
        {
            lblTopTitle.Text = isMuaVao ? "Top 10 nhà cung cấp" : "Top 10 khách hàng";

            var tops = data
                .Select(x => new
                {
                    Mst = isMuaVao ? x.Nbmst : x.Nmmst,
                    Ten = isMuaVao ? x.Nbten : x.Nmten,
                    Tien = x.Tgtttbso
                })
                .Where(a => !string.IsNullOrEmpty(a.Ten))
                .GroupBy(a => string.IsNullOrEmpty(a.Mst) ? a.Ten : a.Mst)
                .Select(g => new { Ten = g.First().Ten, Tien = g.Sum(a => a.Tien) })
                .OrderByDescending(a => a.Tien)
                .Take(10)
                .ToList();

            if (tops.Count == 0) return;
            decimal max = tops.Max(a => a.Tien);
            if (max <= 0) max = 1;

            foreach (var t in tops)
            {
                var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                var dp = new DockPanel();
                dp.Children.Add(new TextBlock
                {
                    Text = t.Tien.ToString("N0"),
                    Foreground = Brush("#fbbf24"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                DockPanel.SetDock(dp.Children[0], Dock.Right);
                dp.Children.Add(new TextBlock
                {
                    Text = t.Ten,
                    Foreground = Brush("#e2e8f0"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                item.Children.Add(dp);

                var track = new Border { Height = 8, Background = Brush("#1f2937"), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 0) };
                var fill = new Border
                {
                    Height = 8,
                    Width = (double)(t.Tien / max) * 360,
                    Background = Brush("#22d3ee"),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                track.Child = fill;
                item.Children.Add(track);

                pnlTop.Children.Add(item);
            }
        }

        private static DateTime? ParseNgay(string raw)
            => DateTime.TryParse(raw, out var d) ? d : (DateTime?)null;

        private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    }
}
