using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Tone controls with the dithered disc preview (the WPF TonePanel).</summary>
internal sealed class TonePanel : IDisposable
{
    private readonly Binder _binder = new();

    public TonePanel(ToneViewModel tone)
    {
        DiscParametersState state = tone.State;

        DiscThumbnail preview = new(148, holeRatio: 15.0 / 60.0);
        Gtk.Label noPreview = Ui.Line(L.T("Xaml.NoPreview"), "dim-label");
        noPreview.SetHalign(Gtk.Align.Center);
        Gtk.Overlay previewOverlay = Gtk.Overlay.New();
        previewOverlay.SetChild(preview.Root);
        previewOverlay.AddOverlay(noPreview);
        _binder.Watch(tone, [nameof(ToneViewModel.TonePreviewImage), nameof(ToneViewModel.HasTonePreview)], () =>
        {
            preview.SetTexture(tone.HasTonePreview && tone.TonePreviewImage is { } image ? TextureFactory.FromBitmap(image) : null);
            noPreview.SetVisible(!tone.HasTonePreview);
        });
        Gtk.Box previewCard = Ui.VBox(0, previewOverlay).Css("inset-panel");
        Gtk.Label status = Ui.Hint(string.Empty);
        _binder.Watch(tone, nameof(ToneViewModel.TonePreviewStatus), () =>
        {
            status.SetLabel(tone.TonePreviewStatus ?? string.Empty);
            status.SetVisible(!string.IsNullOrWhiteSpace(tone.TonePreviewStatus));
        });

        // Dithering 0..1
        Gtk.Label ditherValue = Ui.Line(string.Empty, "numeric");
        _binder.Text(ditherValue, state, nameof(DiscParametersState.ToneDitherText), () => state.ToneDitherText);
        Gtk.Scale dither = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 1, 0.01);
        dither.SetDrawValue(false);
        _binder.Scale(dither, state, nameof(DiscParametersState.ToneDitherStrength), () => state.ToneDitherStrength,
            value => state.ToneDitherStrength = value);

        // Brightness -64..64
        Gtk.Label brightnessValue = Ui.Line(string.Empty, "numeric");
        _binder.Text(brightnessValue, state, nameof(DiscParametersState.ToneBrightnessText), () => state.ToneBrightnessText);
        Gtk.Scale brightness = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, -64, 64, 1);
        brightness.SetDrawValue(false);
        brightness.AddMark(0, Gtk.PositionType.Bottom, null);
        _binder.Scale(brightness, state, nameof(DiscParametersState.ToneBrightness), () => state.ToneBrightness,
            value => state.ToneBrightness = Math.Round(value));

        // CD-only: grey levels and sRGB linearisation.
        Adw.ToggleGroup levels = Ui.Segmented(L.T("Xaml.2Levels"), L.T("Xaml.4Levels"));
        _binder.Segmented(levels, state, nameof(DiscParametersState.IsFourLevelSelected), () => state.IsFourLevelSelected,
            value => state.IsFourLevelSelected = value);
        Gtk.CheckButton srgb = Gtk.CheckButton.NewWithLabel(L.T("Xaml.SRGBLinearizationGamma"));
        _binder.Check(srgb, state, nameof(DiscParametersState.ToneSrgbLinearization), () => state.ToneSrgbLinearization,
            value => state.ToneSrgbLinearization = value);
        Gtk.Box cdBox = Ui.VBox(6,
            Ui.Line(L.T("Xaml.GrayLevels"), "caption-heading", "dim-label"),
            levels,
            Ui.Hint(L.T("Xaml.FourLevelsUseFourTonesToRevealFinerDetails")),
            srgb.Margins(6, 0, 0, 0),
            Ui.Hint(L.T("Xaml.MayImproveTheVisibilityOfTheDiscArtwork")));
        _binder.Visible(cdBox, tone, nameof(ToneViewModel.IsCd), () => tone.IsCd);

        // BD-only: invert 3T/6T mapping.
        Gtk.CheckButton invert = Gtk.CheckButton.NewWithLabel(L.T("Xaml.Invert3T6TMapping"));
        _binder.Check(invert, state, nameof(DiscParametersState.BdInvertMapping), () => state.BdInvertMapping,
            value => state.BdInvertMapping = value);
        Gtk.Box bdBox = Ui.VBox(4, invert, Ui.Hint(L.T("Xaml.SwapDarkAndLightRegionsActualDiscContrastDepends")));
        _binder.Visible(bdBox, tone, nameof(ToneViewModel.IsBluRay), () => tone.IsBluRay);

        Gtk.Button reset = Ui.Button(L.T("Xaml.ResetDefaults"));
        _binder.Command(reset, tone.ResetToneCommand);
        Gtk.Button refresh = Ui.Button(L.T("Xaml.RefreshPreview"));
        _binder.Command(refresh, tone.RefreshTonePreviewCommand);

        Root = Ui.VBox(10,
            Ui.Line(L.T("Xaml.Tone"), "heading"),
            previewCard,
            status,
            Ui.HBox(8, Ui.Line(L.T("Xaml.Dithering"), "caption-heading", "dim-label").Expand(), ditherValue),
            dither,
            Ui.HBox(8, Ui.Line(L.T("Xaml.Brightness"), "caption-heading", "dim-label").Expand(), brightnessValue),
            brightness,
            cdBox,
            bdBox,
            Ui.HBox(8, reset, refresh).Margins(6, 0, 0, 0));
    }

    public Gtk.Box Root { get; }

    public void Dispose() => _binder.Dispose();
}
