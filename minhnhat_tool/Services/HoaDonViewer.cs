using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using QRCoder;
using minhnhat_tool.Models;

namespace minhnhat_tool.Services
{
    /// <summary>
    /// Xem hóa đơn giống bản trên hoadondientu: dựng XML đầy đủ từ CHI TIẾT -> XSLT -> HTML (kèm QR).
    /// </summary>
    public static class HoaDonViewer
    {
        private const string XsltText = @"<?xml version='1.0' encoding='UTF-8'?>
<xsl:stylesheet version='1.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
  <xsl:output method='html' encoding='UTF-8' indent='yes'/>
  <xsl:template match='/hoadon'>
    <html>
      <head>
        <meta charset='UTF-8'/>
        <style>
          body { font-family:'Times New Roman',serif; background:#e9edf2; padding:22px; color:#111; font-size:14px; }
          .inv { max-width:920px; margin:auto; background:#fff; border:1px solid #d0d7de; padding:34px 40px; }
          .top { display:flex; justify-content:space-between; align-items:flex-start; }
          .top .r { text-align:right; }
          h1 { text-align:center; font-size:23px; margin:6px 0 2px; }
          .date { text-align:center; font-style:italic; }
          .mccqt { text-align:center; font-size:13px; color:#333; margin-bottom:14px; }
          .hr { border-top:1px solid #b7860b; margin:10px 0; }
          .fld { margin:2px 0; }
          .fld b { font-weight:bold; }
          table { width:100%; border-collapse:collapse; margin-top:12px; font-size:13px; }
          th,td { border:1px solid #333; padding:5px 7px; vertical-align:top; }
          th { background:#f3f4f6; text-align:center; font-weight:bold; }
          .c { text-align:center; } .r { text-align:right; }
          .tot td { border:1px solid #333; padding:5px 8px; }
          .totwrap { display:flex; margin-top:12px; gap:0; }
          .totleft { width:38%; } .totright { width:62%; }
          .big { font-weight:bold; }
          .words { font-style:italic; margin-top:6px; }
          .sign { display:flex; justify-content:space-around; margin-top:30px; text-align:center; }
          .valid { color:#15803d; border:1px solid #15803d; padding:6px 12px; display:inline-block; font-size:12px; }
        </style>
      </head>
      <body>
        <div class='inv'>
          <div class='top'>
            <div>
              <img style='width:96px;height:96px'>
                <xsl:attribute name='src'><xsl:value-of select='qr'/></xsl:attribute>
              </img>
            </div>
            <div class='r'>
              <div>Mẫu số: <xsl:value-of select='@mau'/></div>
              <div>Ký hiệu: <b><xsl:value-of select='@kyhieu'/></b></div>
              <div>Số: <b><xsl:value-of select='@so'/></b></div>
            </div>
          </div>
          <h1>HOÁ ĐƠN GIÁ TRỊ GIA TĂNG</h1>
          <div class='date'><xsl:value-of select='ngay'/></div>
          <div class='mccqt'>MCCQT: <xsl:value-of select='mccqt'/></div>
          <div class='hr'></div>

          <div class='fld'><b>Tên người bán:</b> <xsl:value-of select='nban/@ten'/></div>
          <div class='fld'><b>Mã số thuế:</b> <xsl:value-of select='nban/@mst'/></div>
          <div class='fld'><b>Địa chỉ:</b> <xsl:value-of select='nban/@dchi'/></div>
          <div class='fld'><b>Điện thoại:</b> <xsl:value-of select='nban/@dthoai'/></div>
          <div class='fld'><b>Số tài khoản:</b> <xsl:value-of select='nban/@stk'/> &#160; <xsl:value-of select='nban/@nhang'/></div>
          <div class='hr'></div>

          <div class='fld'><b>Tên người mua:</b> <xsl:value-of select='nmua/@ten'/></div>
          <div class='fld'><b>Họ tên người mua hàng:</b> <xsl:value-of select='nmua/@hoten'/></div>
          <div class='fld'><b>Mã số thuế:</b> <xsl:value-of select='nmua/@mst'/></div>
          <div class='fld'><b>Địa chỉ:</b> <xsl:value-of select='nmua/@dchi'/></div>
          <div class='fld'><b>Số tài khoản:</b> <xsl:value-of select='nmua/@stk'/> &#160; <xsl:value-of select='nmua/@nhang'/></div>
          <div class='fld'><b>Hình thức thanh toán:</b> <xsl:value-of select='nmua/@htttoan'/> &#160;&#160; <b>Đơn vị tiền tệ:</b> <xsl:value-of select='nmua/@dvtte'/></div>

          <table>
            <tr>
              <th>STT</th><th>Tính chất</th><th>Loại hàng hóa đặc trưng</th><th>Tên hàng hóa, dịch vụ</th>
              <th>Đơn vị tính</th><th>Số lượng</th><th>Đơn giá</th><th>Chiết khấu</th><th>Thuế suất</th><th>Thành tiền chưa có thuế GTGT</th>
            </tr>
            <xsl:for-each select='items/item'>
              <tr>
                <td class='c'><xsl:value-of select='@stt'/></td>
                <td><xsl:value-of select='@tinhchat'/></td>
                <td><xsl:value-of select='@loai'/></td>
                <td><xsl:value-of select='@ten'/></td>
                <td class='c'><xsl:value-of select='@dvt'/></td>
                <td class='r'><xsl:value-of select='@sl'/></td>
                <td class='r'><xsl:value-of select='@dg'/></td>
                <td class='r'><xsl:value-of select='@ck'/></td>
                <td class='c'><xsl:value-of select='@ts'/></td>
                <td class='r'><xsl:value-of select='@tt'/></td>
              </tr>
            </xsl:for-each>
          </table>

          <div class='totwrap'>
            <table class='tot totleft'>
              <tr><th>Thuế suất</th><th>Tổng tiền chưa thuế</th><th>Tổng tiền thuế</th></tr>
              <tr><td class='c'><xsl:value-of select='tsuat'/></td><td class='r'><xsl:value-of select='chuathue'/></td><td class='r'><xsl:value-of select='thue'/></td></tr>
            </table>
            <table class='tot totright'>
              <tr><td>Tổng tiền chưa thuế</td><td class='r'><xsl:value-of select='chuathue'/></td></tr>
              <tr><td>Tổng tiền thuế</td><td class='r'><xsl:value-of select='thue'/></td></tr>
              <tr><td>Tổng tiền chiết khấu thương mại</td><td class='r'><xsl:value-of select='ck'/></td></tr>
              <tr><td>Tổng tiền phí</td><td class='r'><xsl:value-of select='phi'/></td></tr>
              <tr><td class='big'>Tổng tiền thanh toán</td><td class='r big'><xsl:value-of select='thanhtoan'/></td></tr>
            </table>
          </div>
          <div class='words'>Số tiền viết bằng chữ: <xsl:value-of select='tienchu'/></div>

          <div class='sign'>
            <div><b>NGƯỜI MUA HÀNG</b><br/><i>(Chữ ký số - nếu có)</i></div>
            <div><b>NGƯỜI BÁN HÀNG</b><br/><span class='valid'>Signature Valid &#10003;<br/>Ký bởi: <xsl:value-of select='nban/@ten'/><br/>Ký ngày: <xsl:value-of select='ngayky'/></span></div>
          </div>
        </div>
      </body>
    </html>
  </xsl:template>
</xsl:stylesheet>";

        public static void ShowInvoice(HoaDonInfo hd, string tenDN, bool isMuaVao, string detailJson)
        {
            var xml = BuildXml(hd, tenDN, isMuaVao, detailJson);
            string html = Transform(xml);
            string path = Path.Combine(Path.GetTempPath(), $"hoadon_{hd.Shdon}_{Guid.NewGuid():N}.html");
            File.WriteAllText(path, html, new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private static string Transform(XDocument xml)
        {
            var xslt = new XslCompiledTransform();
            using (var sr = new StringReader(XsltText))
            using (var xr = XmlReader.Create(sr))
                xslt.Load(xr);
            var sb = new StringBuilder();
            using (var w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true }))
                xslt.Transform(xml.CreateReader(), w);
            return sb.ToString();
        }

        private static XDocument BuildXml(HoaDonInfo hd, string tenDN, bool isMuaVao, string detailJson)
        {
            // Mặc định từ tóm tắt
            string mau = hd.Khmshdon, kyhieu = hd.Khhdon, so = hd.Shdon;
            string ngay = FmtNgayFull(hd.Tdlap), ngayky = "", mccqt = "", qr = "";
            string nbTen = isMuaVao ? hd.Nbten : tenDN, nbMst = hd.Nbmst, nbDchi = "", nbDt = "", nbStk = "", nbNh = "";
            string nmTen = isMuaVao ? tenDN : hd.Nmten, nmHoten = "", nmMst = hd.Nmmst, nmDchi = "", nmStk = "", nmNh = "";
            string htttoan = "", dvtte = "VND", tienchu = "", tsuat = "";
            decimal chua = hd.Tgtcthue, thue = hd.Tgtthue, tong = hd.Tgtttbso, ck = hd.Ttcktmai, phi = 0;
            var items = new XElement("items");

            if (!string.IsNullOrEmpty(detailJson))
            {
                try
                {
                    var r = JsonDocument.Parse(detailJson).RootElement;
                    mau = S(r, "khmshdon"); kyhieu = S(r, "khhdon"); so = S(r, "shdon");
                    ngay = FmtNgayFull(S(r, "tdlap")); ngayky = FmtDateTime(S(r, "nky"));
                    mccqt = S(r, "mhdon"); qr = QrDataUri(S(r, "qrcode"));
                    nbTen = S(r, "nbten"); nbMst = S(r, "nbmst"); nbDchi = S(r, "nbdchi");
                    nbDt = S(r, "nbsdthoai"); nbStk = S(r, "nbstkhoan"); nbNh = S(r, "nbtnhang");
                    nmTen = S(r, "nmten"); nmHoten = S(r, "nmtnmua"); nmMst = S(r, "nmmst"); nmDchi = S(r, "nmdchi");
                    nmStk = S(r, "nmstkhoan"); nmNh = S(r, "nmtnhang");
                    htttoan = S(r, "thtttoan"); dvtte = Def(S(r, "dvtte"), "VND"); tienchu = S(r, "tgtttbchu");
                    chua = D(r, "tgtcthue"); thue = D(r, "tgtthue"); tong = D(r, "tgtttbso");
                    ck = D(r, "ttcktmai"); phi = D(r, "tgtphi");

                    if (r.TryGetProperty("hdhhdvu", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var it in arr.EnumerateArray())
                        {
                            if (string.IsNullOrEmpty(tsuat)) tsuat = S(it, "ltsuat");
                            items.Add(new XElement("item",
                                new XAttribute("stt", S(it, "stt")),
                                new XAttribute("tinhchat", TinhChat((int)D(it, "tchat"))),
                                new XAttribute("loai", ""),
                                new XAttribute("ten", S(it, "ten")),
                                new XAttribute("dvt", S(it, "dvtinh")),
                                new XAttribute("sl", D(it, "sluong").ToString("N0")),
                                new XAttribute("dg", D(it, "dgia").ToString("N0")),
                                new XAttribute("ck", D(it, "stckhau").ToString("N0")),
                                new XAttribute("ts", S(it, "ltsuat")),
                                new XAttribute("tt", D(it, "thtien").ToString("N0"))));
                        }
                }
                catch { }
            }

            return new XDocument(new XElement("hoadon",
                new XAttribute("mau", mau), new XAttribute("kyhieu", kyhieu), new XAttribute("so", so),
                new XElement("qr", qr),
                new XElement("ngay", ngay), new XElement("ngayky", ngayky), new XElement("mccqt", mccqt),
                new XElement("nban", new XAttribute("ten", nbTen), new XAttribute("mst", nbMst),
                    new XAttribute("dchi", nbDchi), new XAttribute("dthoai", nbDt),
                    new XAttribute("stk", nbStk), new XAttribute("nhang", nbNh)),
                new XElement("nmua", new XAttribute("ten", nmTen), new XAttribute("hoten", nmHoten),
                    new XAttribute("mst", nmMst), new XAttribute("dchi", nmDchi),
                    new XAttribute("stk", nmStk), new XAttribute("nhang", nmNh),
                    new XAttribute("htttoan", htttoan), new XAttribute("dvtte", dvtte)),
                items,
                new XElement("tsuat", tsuat),
                new XElement("chuathue", chua.ToString("N0")),
                new XElement("thue", thue.ToString("N0")),
                new XElement("ck", ck.ToString("N0")),
                new XElement("phi", phi.ToString("N0")),
                new XElement("thanhtoan", tong.ToString("N0")),
                new XElement("tienchu", tienchu)));
        }

        private static string QrDataUri(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            try
            {
                using var gen = new QRCodeGenerator();
                using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
                var png = new PngByteQRCode(data);
                return "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6));
            }
            catch { return ""; }
        }

        private static string TinhChat(int t) => t switch
        {
            1 => "Hàng hóa, dịch vụ",
            2 => "Khuyến mại",
            3 => "Chiết khấu thương mại",
            4 => "Ghi chú/Diễn giải",
            _ => ""
        };

        private static string S(JsonElement e, string n)
            => e.TryGetProperty(n, out var v)
               ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ValueKind == JsonValueKind.Number ? v.ToString() : "")
               : "";
        private static decimal D(JsonElement e, string n)
            => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
        private static string Def(string s, string d) => string.IsNullOrEmpty(s) ? d : s;
        private static string FmtNgayFull(string raw)
            => DateTime.TryParse(raw, out var d) ? $"Ngày {d:dd} tháng {d:MM} năm {d:yyyy}" : raw;
        private static string FmtDateTime(string raw)
            => DateTime.TryParse(raw, out var d) ? d.ToString("dd/MM/yyyy HH:mm:ss") : raw;
    }
}
