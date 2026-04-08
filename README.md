# EFT DMA Radar — Silk.NET Edition

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

## Donate

If you find this project useful, consider supporting the development:

- **Keeegi** — Silk.NET rewrite author
  - PayPal: https://www.paypal.com/paypalme/huiteab

---

## Special Thanks

- **x0m** — original WPF codebase this project is based on
  - Paypal: https://www.paypal.me/eftx0m?locale.x=en_NZ
  - BTC: `1AzMqjpjaN5fyGQgZTByRqA2CzKHQSXkMr`
  - LTC: `LWi2mP6GaDQbhDAzs4swiSEEowETRqCcLZ`
  - ETH: `0x6fe7aee467b63fde7dbbf478dce6a0d7695ae496`
  - USDT: `TYNZr9FL5dVtk1K5D5AwbiWt4UMbu9A7E3`
- **Mambo** — significant contributions to the original WPF codebase
  - PayPal: https://paypal.me/MamboNoob?country.x=CA&locale.x=en_US
  - BTC: `bc1qgw9v6xtwxqhtsuuge720lr5vrhfv596wqml2mk`
- **Marazm** — keeping EFT maps updated
  - https://boosty.to/dma_maps/donate
  - USDT: `TWeRAuxsCFa8BHLZZbkz9aUHJcZwnGkiJx`
  - BTC: `bc1q32enxjvfvzp30rpm39uzgwpdxcl57l264reevu`

---

## Contact

Join the [Discord Server](https://discord.gg/QxAeaYzF) for help and updates.

