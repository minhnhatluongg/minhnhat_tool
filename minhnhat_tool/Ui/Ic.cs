using System.Windows;
using System.Windows.Media;

namespace minhnhat_tool.Ui
{
    /// <summary>
    /// Gắn icon vào Button / MenuItem mà không phải viết StackPanel + Path cho từng nút.
    ///
    ///     &lt;Button Content="Chạy ngay" ui:Ic.Glyph="{StaticResource IcChay}"/&gt;
    ///
    /// Mẫu nút trong Assets/Controls.xaml đọc thuộc tính này; không gán thì phần icon
    /// tự thu về 0 và nút hiển thị y như cũ.
    /// </summary>
    public static class Ic
    {
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.RegisterAttached(
                "Glyph", typeof(Geometry), typeof(Ic), new PropertyMetadata(null));

        public static void SetGlyph(DependencyObject o, Geometry v) => o.SetValue(GlyphProperty, v);
        public static Geometry GetGlyph(DependencyObject o) => (Geometry)o.GetValue(GlyphProperty);

        /// <summary>Cỡ icon trong nút (mặc định 16). Nút to thì tăng lên cho cân.</summary>
        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.RegisterAttached(
                "Size", typeof(double), typeof(Ic), new PropertyMetadata(16.0));

        public static void SetSize(DependencyObject o, double v) => o.SetValue(SizeProperty, v);
        public static double GetSize(DependencyObject o) => (double)o.GetValue(SizeProperty);
    }
}
