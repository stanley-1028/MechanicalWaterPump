# MWP mod — developer notes

Build/package entry point: `build.ps1` (see `docs/` for the full contract).

## Rules that are not optional on this machine

1. Every `dotnet build`/`msbuild` call uses **`-m:1`**; parallel MSBuild fails silently (exit 1,
   no messages).
2. A `.csproj` may declare **at most one `ProjectReference`** (same silent restore failure).
   Dependency graph: `Core <- Gameplay`, `Core <- Integration`, `Core <- UI`.
3. No network: only the local NuGet cache. Tests and tools must not add NuGet packages
   (Mono.Cecil is referenced by `HintPath` from `%UserProfile%\.nuget\packages`).
4. `TreatWarningsAsErrors=true` in every project; the staging build must be **0 warnings**.
5. Game-facing assemblies target `netstandard2.1` and reference the installed game DLLs with
   `Private=false` (`Directory.Build.targets`). Core must never reference game assemblies.

## Where the ground truth lives

- `docs/API_FACTS_MWP.md` — verified game API facts (metadata + IL evidence in `docs/api/`).
  Re-run the dumper instead of guessing:
  `dotnet tools/ApiIndex/bin/Release/net9.0/ApiIndex.dll dump "<game>\Timberborn_Data\Managed" out.txt "<TypeRegex>"`
- `docs/CONTRACT.md` — frozen implementation contract (spec fields, blueprint shape, save keys,
  localization keys, state machine rules). Change it only via the Lead.
- `../機械水泵模組需求文檔.md` — requirements; chapter 20 wins over chapters 1–19.

## Verification before claiming anything works

```powershell
pwsh -File build.ps1 -Clean          # builds 4 assemblies, runs tests, validates data, stages the mod
```

Reports must quote real output (`PASS n/n`, `VALIDATION PASSED`, `0 warning`). In-game acceptance
(T01–T27) cannot be automated from this machine; it is tracked in `docs/VERIFICATION.md`.
