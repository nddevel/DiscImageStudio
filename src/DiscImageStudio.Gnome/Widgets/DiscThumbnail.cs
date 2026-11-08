namespace DiscImageStudio.Gnome.Widgets;

/// <summary>A small disc: the picture clipped to a circle with the centre hole punched out.
/// Used for recent-project thumbnails and the tone preview.</summary>
internal sealed class DiscThumbnail
{
    private readonly Gtk.DrawingArea _area;
    private Gdk.Texture? _texture;
    private Cairo.ImageSurface? _surface;

    public DiscThumbnail(int size, double holeRatio = 0.125)
    {
        HoleRatio = holeRatio;
        _area = Gtk.DrawingArea.New();
        _area.SetContentWidth(size);
        _area.SetContentHeight(size);
        _area.SetValign(Gtk.Align.Center);
        _area.SetHalign(Gtk.Align.Center);
        _area.SetDrawFunc(Draw);
    }

    public Gtk.Widget Root => _area;

    public double HoleRatio { get; set; }

    public void SetTexture(Gdk.Texture? texture)
    {
        _texture = texture;
        _surface?.Dispose();
        _surface = texture is null ? null : Imaging.TextureSurfaces.ToSurface(texture);
        _area.QueueDraw();
    }

    private void Draw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        double size = Math.Min(width, height);
        double radius = size / 2 - 1;
        double cx = width / 2.0, cy = height / 2.0;
        area.GetColor(out Gdk.RGBA fg);

        cr.Save();
        cr.Arc(cx, cy, radius, 0, 2 * Math.PI);
        cr.Arc(cx, cy, radius * HoleRatio, 0, 2 * Math.PI);
        cr.FillRule = Cairo.FillRule.EvenOdd;
        if (_surface is { } surface)
        {
            cr.Clip();
            cr.Translate(cx - radius, cy - radius);
            cr.Scale(2 * radius / surface.Width, 2 * radius / surface.Height);
            cr.SetSourceSurface(surface, 0, 0);
            using (Cairo.Pattern pattern = cr.GetSource()) pattern.Filter = Cairo.Filter.Good;
            cr.Paint();
        }
        else
        {
            cr.SetSourceRgba(fg.Red, fg.Green, fg.Blue, 0.08);
            cr.Fill();
        }

        cr.Restore();
        cr.SetSourceRgba(fg.Red, fg.Green, fg.Blue, 0.18);
        cr.LineWidth = 1;
        cr.Arc(cx, cy, radius, 0, 2 * Math.PI);
        cr.Stroke();
        cr.Arc(cx, cy, radius * HoleRatio, 0, 2 * Math.PI);
        cr.Stroke();
    }
}
