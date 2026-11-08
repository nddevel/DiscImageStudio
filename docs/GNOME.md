# Disc Image Studio for GNOME (GTK 4 / libadwaita)

This fork adds a native Linux desktop frontend, `src/DiscImageStudio.Gnome`, built with
GTK 4 and libadwaita through the [GirCore](https://gircore.github.io/) .NET bindings. It
offers the same workflow as the Windows application:

| Windows (WPF) | GNOME (GTK 4) |
| --- | --- |
| Home: templates and recent projects | Same, with libadwaita cards and a header-bar menu |
| New project / project settings window | `AdwDialog` with template browser, geometry fields and save location |
| Design: layers, disc canvas, properties, tone | Same three-column layout; Cairo disc canvas with move / scale / rotate handles, Ctrl+scroll zoom, middle-drag pan |
| Preview: design projection and output read-back | Same inspector and result monitor |
| Export: ISO, WAV + CUE, RAW | libadwaita preference rows bound to the same options |
| Live burning (IMAPI2) | Live burning through `cdrskin` (CD) and `xorriso` (DVD, BD-R), streamed from memory like on Windows |
| Assisted calibration | Same three phases, step cards and interactive photo canvas |
| Settings / About | `AdwDialog` preferences, `AdwAboutDialog`, keyboard-shortcuts dialog |
| Toasts, confirmation modals, job dock | `AdwToastOverlay`, `AdwAlertDialog`, job dock with progress and log |

All seven UI languages from the Windows app are available and follow the same setting.

## How it is built

The GTK project compiles the WPF application's view models, services and localisation
catalogues **unchanged** (`src/DiscImageStudio.App/ViewModels`, `Services`, `Localization`).
`src/DiscImageStudio.Gnome/Compat` supplies the few WPF types those files use (a GLib-based
`Dispatcher`/`DispatcherTimer`, a managed `BitmapSource`, `Visibility`), and the GTK views bind
to the view models through `Widgets/Binder.cs`. Behaviour therefore stays identical to the
Windows app, and upstream changes to the shared logic flow into the GNOME build when the fork
is synced.

Windows-only pieces are replaced:

- `Services/DesignComposer.cs`: layer compositing with Cairo (WPF used `DrawingVisual`).
- `Services/FileDialogService.cs`: GTK file dialogs (desktop portal on GNOME).
- `Burning/LinuxOpticalDiscBurner.cs`: device discovery from sysfs, media and speed queries
  with `xorriso`, recording with `cdrskin`/`wodim` (CD, session-at-once) and `xorriso`
  (DVD, BD-R with stream recording).

## Running from source

Requirements: .NET 10 SDK, GTK ≥ 4.14, libadwaita ≥ 1.7. Burning additionally needs
`cdrskin` (from libburn) or `wodim`, and `xorriso`, plus read/write access to `/dev/sr*`
(usually the `cdrom` or `optical` group).

```sh
dotnet run --project src/DiscImageStudio.Gnome
```

On NixOS, `shell.nix` provides the SDK and the libraries GirCore loads at runtime:

```sh
nix-shell --run 'dotnet run --project src/DiscImageStudio.Gnome'
```

The executable also accepts the CLI generation commands
(`disc-image-studio-gnome cd-generate …`, `solve …`, `bd-generate …`) and `.discproj`
files to open.

## Installing

Any distribution (self-contained build into `~/.local`, with desktop entry, icon and
`.discproj` file type):

```sh
packaging/linux/install.sh
packaging/linux/install.sh --uninstall
```

NixOS / Nix:

```sh
nix-build packaging/nix            # ./result/bin/disc-image-studio-gnome
```

or add `(pkgs.callPackage /path/to/DiscImageStudio/packaging/nix/package.nix { })` to
`environment.systemPackages`. After changing NuGet references, refresh the lock file with
`$(nix-build packaging/nix -A fetch-deps --no-out-link) packaging/nix/deps.json`.

## Notes and limitations

- Live burning on Linux is experimental and has not yet been validated on physical drives.
  Use expendable media, exactly as on Windows. Exporting an image and burning it with your
  preferred tool remains the safest path.
- The CD audio stream is sent as little-endian PCM with `-swab`, in session-at-once mode, so
  the track starts at the same position IMAPI2's disc-at-once writes it.
- For scripted screenshots, `DIS_GTK_DEBUG_PAGE=Design|Preview|Export|Burn|Calibration` opens a
  page once a project is loaded and `DIS_GTK_DEBUG_ACTION=new-project|preferences|about`
  triggers a window action.
