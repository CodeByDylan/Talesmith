{
  description = "Talesmith, a 2D game engine and editor for .NET";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { nixpkgs, ... }:
    let
      pkgs = nixpkgs.legacyPackages.x86_64-linux;
    in
    {
      packages.x86_64-linux.default = pkgs.callPackage ./packaging/nix/package.nix { };
      overlays.default = final: _: { talesmith = final.callPackage ./packaging/nix/package.nix { }; };
    };
}
