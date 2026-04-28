# EFT DMA Radar — Silk.NET Edition

> **⚠️ This project is no longer actively maintained.**
> No further updates, bug fixes, or support will be provided.

A **DMA radar** for Escape from Tarkov built on Silk.NET + SkiaSharp + ImGui.

---

## Requirements

- A **DMA card** supported by [MemProcFS](https://github.com/ufrisk/MemProcFS) (e.g. `fpga`, `usb3380`)
- Windows 10 / 11 (x64)
- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

---

## Getting Started

1. Clone or download and extract the repository
2. Open `eft-dma-radar.sln` in Visual Studio 2022+
3. Set `eft-dma-radar-silk` as the startup project
4. Build and run

Configuration is saved to `%AppData%\eft-dma-radar-silk\config.json`.

Game offsets are resolved at startup via the built-in IL2CPP dumper and cached to `%AppData%\eft-dma-radar-silk\il2cpp_offsets.json`. The hardcoded values in `src-silk/Tarkov/Offsets.cs` serve as a fallback when no valid cache exists.

---

## Special Thanks

- **x0m** — original WPF codebase this project is based on
- **Mambo** — significant contributions to the original WPF codebase
- **Marazm** — keeping EFT maps updated