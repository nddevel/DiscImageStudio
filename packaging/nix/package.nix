# Nix package for the GTK 4 / libadwaita frontend.
#   nix-build packaging/nix                       # build ./result/bin/disc-image-studio-gnome
#   environment.systemPackages = [ (pkgs.callPackage /path/to/packaging/nix/package.nix { }) ];
# After changing NuGet references, refresh the lock file:
#   $(nix-build packaging/nix -A fetch-deps --no-out-link) packaging/nix/deps.json
{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  wrapGAppsHook4,
  adwaita-icon-theme,
  glib,
  gtk4,
  libadwaita,
  cairo,
  pango,
  gdk-pixbuf,
  graphene,
  harfbuzz,
  libburn,
  xorriso,
}:

buildDotnetModule {
  pname = "disc-image-studio-gnome";
  version = "1.5.0";

  src = lib.cleanSourceWith {
    src = ../..;
    filter = path: type:
      let base = baseNameOf path; in
      !(type == "directory" && (base == "bin" || base == "obj" || base == ".git" || base == "artifacts"))
      && !(lib.hasSuffix ".png" base && lib.hasInfix "/docs/" path);
  };

  projectFile = "src/DiscImageStudio.Gnome/DiscImageStudio.Gnome.csproj";
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;
  executables = [ "disc-image-studio-gnome" ];

  nativeBuildInputs = [ wrapGAppsHook4 ];
  buildInputs = [ gtk4 libadwaita adwaita-icon-theme ];
  # GirCore loads these with dlopen; buildDotnetModule adds them to LD_LIBRARY_PATH.
  runtimeDeps = [ glib gtk4 libadwaita cairo pango gdk-pixbuf graphene harfbuzz ];

  dontWrapGApps = true;
  preFixup = ''
    makeWrapperArgs+=("''${gappsWrapperArgs[@]}")
    makeWrapperArgs+=(--prefix PATH : ${lib.makeBinPath [ libburn xorriso ]})
  '';

  postInstall = ''
    install -Dm644 packaging/linux/io.github.nddevel.DiscImageStudio.desktop \
      $out/share/applications/io.github.nddevel.DiscImageStudio.desktop
    install -Dm644 packaging/linux/io.github.nddevel.DiscImageStudio.metainfo.xml \
      $out/share/metainfo/io.github.nddevel.DiscImageStudio.metainfo.xml
    install -Dm644 packaging/linux/io.github.nddevel.DiscImageStudio.mime.xml \
      $out/share/mime/packages/io.github.nddevel.DiscImageStudio.xml
    install -Dm644 packaging/linux/io.github.nddevel.DiscImageStudio.png \
      $out/share/icons/hicolor/256x256/apps/io.github.nddevel.DiscImageStudio.png
  '';

  meta = {
    description = "Create and burn visible artwork on optical discs (GTK 4 / libadwaita)";
    homepage = "https://github.com/nddevel/DiscImageStudio";
    license = lib.licenses.gpl3Plus;
    mainProgram = "disc-image-studio-gnome";
    platforms = [ "x86_64-linux" "aarch64-linux" ];
  };
}
