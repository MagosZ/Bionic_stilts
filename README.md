# Stilts Bionics

A RimWorld mod (1.6 and 1.7): bionic legs with telescopic stilts. Extended, a colonist stands six metres tall, shoots
further, looks down over cover and over the wall next to them, and stops to retract at the edge of every roof.
Works with or without Combat Extended.

- Spec: [DESIGN.md](DESIGN.md). In-game checks: [TESTING.md](TESTING.md).
- Requires Harmony. Combat Extended optional (the CE rules are a second DLL in `CE/`, loaded only with CE).
- Build: `dotnet build Source/CE/StiltsBionicsCE.csproj -c Release` builds both DLLs.
  Textures: `py -3 Source/Art/make_textures.py`.

Licence: CC BY-NC-SA 4.0 (see [LICENSE](LICENSE)); the CE rules adapt Combat Extended code.
