# Development shell for the GTK 4 frontend on NixOS / Nix:
#   nix-shell --run 'dotnet run --project src/DiscImageStudio.Gnome'
{ pkgs ? import <nixpkgs> { } }:
let
  runtimeLibs = with pkgs; [ glib gtk4 libadwaita cairo pango gdk-pixbuf graphene harfbuzz ];
in
pkgs.mkShell {
  packages = with pkgs; [ dotnet-sdk_10 gtk4 libburn xorriso ];
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibs;
  XDG_DATA_DIRS = "${pkgs.gsettings-desktop-schemas}/share/gsettings-schemas/${pkgs.gsettings-desktop-schemas.name}:${pkgs.gtk4}/share/gsettings-schemas/${pkgs.gtk4.name}:${pkgs.adwaita-icon-theme}/share";
  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
}
