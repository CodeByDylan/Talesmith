# The editor from a Linux release archive, with the libraries it loads from nixpkgs. The players stay as released, because exported
# games copy them and have to run on any Linux distribution.
{
  lib,
  stdenv,
  fetchurl,
  autoPatchelfHook,
  makeWrapper,
  alsa-lib,
  fontconfig,
  gtk3,
  libGL,
  libice,
  libpulseaudio,
  libsm,
  libx11,
  libxcursor,
  libxext,
  libxfixes,
  libxi,
  libxrandr,
  pipewire,
  vulkan-loader,
  release ? lib.importJSON ./release.json,
  archive ? fetchurl {
    url = "https://github.com/CodeByDylan/Talesmith/releases/download/v${release.version}/talesmith-${release.version}-linux-x64.tar.gz";
    hash = release.hash or (throw "No Talesmith release is packaged for Nix yet");
  },
}:

stdenv.mkDerivation {
  pname = "talesmith";
  version = release.version or (throw "No Talesmith release is packaged for Nix yet");
  src = archive;

  nativeBuildInputs = [
    autoPatchelfHook
    makeWrapper
  ];
  buildInputs = [
    fontconfig
    stdenv.cc.cc.lib
  ];
  # .NET loads these by name at run time, from its own libraries, so the editor finds them on its library path.
  libraryPath = lib.makeLibraryPath [
    alsa-lib
    gtk3
    libGL
    libice
    libpulseaudio
    libsm
    libx11
    libxcursor
    libxext
    libxfixes
    libxi
    libxrandr
    pipewire
    vulkan-loader
  ];

  # .NET's optional LTTng tracing provider goes unused, and .NET loads the bundled ICU libraries by full path, each before the next.
  autoPatchelfIgnoreMissingDeps = [
    "liblttng-ust.so.0"
    "libicudata.so.*"
    "libicuuc.so.*"
  ];

  dontConfigure = true;
  dontBuild = true;
  dontStrip = true;
  dontAutoPatchelf = true;

  installPhase = ''
    runHook preInstall
    mkdir -p $out/lib $out/bin
    cp -r lib/talesmith $out/lib/talesmith
    makeWrapper $out/lib/talesmith/talesmith $out/bin/talesmith --prefix LD_LIBRARY_PATH : "$libraryPath"
    install -Dm644 ${../linux/talesmith.desktop} $out/share/applications/talesmith.desktop
    install -Dm644 ${../../website/static/img/logo.svg} $out/share/icons/hicolor/scalable/apps/talesmith.svg
    runHook postInstall
  '';

  postFixup = ''
    autoPatchelf --no-recurse $out/lib/talesmith
  '';

  meta = {
    description = "2D game engine and editor for .NET";
    homepage = "https://talesmith.dev";
    license = lib.licenses.asl20;
    mainProgram = "talesmith";
    platforms = [ "x86_64-linux" ];
    sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
  };
}
