[![Twitter URL](https://img.shields.io/twitter/url/https/twitter.com/deanthecoder.svg?style=social&label=Follow%20%40deanthecoder)](https://twitter.com/deanthecoder)
[![GitHub Repo stars](https://img.shields.io/github/stars/deanthecoder/ZXBasic?style=social&label=Star)](https://github.com/deanthecoder/ZXBasic/stargazers)

# ZXBasic

**ZX Spectrum BASIC, rebuilt as a modern cross-platform Avalonia application.**

ZXBasic recreates the friendly immediacy of programming a ZX Spectrum without emulating its Z80 processor. Enter numbered Sinclair BASIC lines, type `RUN`, and watch them execute on an authentic 256×192 display—with color clash, ROM lettering, flashing cursor modes, and an optional phosphor CRT effect.

![ZXBasic showing an imported Human Shader BASIC listing](img/ZXBasic.png)

## Highlights

- Sinclair BASIC syntax with numbered programs and immediate commands.
- Native keyboard entry, automatic uppercase conversion, multiline paste, and `GOTO`/`GOSUB` aliases.
- Click a listed line to edit it; use arrows, Home, End, Backspace, and Delete normally.
- Scroll long listings with the mouse wheel.
- Authentic 256×192 bitmap, 32×24 attributes, Spectrum palette, bright and flash colors, border, and color clash.
- ZXSpeculator-style CRT rendering with RGB phosphors, scanlines, grain, saturation, vignetting, and soft glow.
- Approximate Spectrum BASIC speed, 10× Fast mode, and Unlimited execution for demanding programs.
- Press Escape at any time to break into a running program.
- Import tokenized BASIC from 48K `.sna` snapshots through the File menu, `Ctrl/Command+O`, or drag and drop.
- Live `PEEK` and `POKE` access to Spectrum bitmap and attribute memory.
- `RENUM` / `RENUMBER` extension for modern convenience.

## Included examples

Two complete programs are included as readable BASIC and ready-to-open snapshots:

- [Human Shader](Examples/HumanShader/HumanShader.bas) — a substantial graphics program inspired by [humanshader.com](https://humanshader.com/). Open [HumanShader.sna](Examples/HumanShader/HumanShader.sna) and select Unlimited speed unless you fancy the original wait.
- [Conway's Game of Life](Examples/Conway/Conway.bas) — uses the Spectrum attribute map as both its display and working data. Open [Conway.sna](Examples/Conway/Conway.sna) to run it directly.

![Conway's Game of Life running through BASIC PEEK and POKE](img/GameOfLife.png)

## Using ZXBasic

Type a numbered line and press Enter to add or replace it:

```basic
10 BORDER 1
20 FOR A=-99 TO 99
30 PLOT A+110,80+30*SIN (A/12)
40 NEXT A
50 PAUSE 0
```

The program automatically lists around the line you entered. The `>` marker follows the newest or selected line. Entering a line number on its own deletes that line.

Useful immediate commands include:

```basic
LIST
RUN
RUN 100
BORDER 3: PAPER 2: CLS
RENUMBER 10,10
NEW
```

Paste several numbered lines at once to add a complete listing. Direct commands can also contain multiple statements separated by colons.

The walking/running toolbar icon rotates between Spectrum, Fast, and Unlimited speed. You can change speed while a program is running. Display → CRT effect switches between the processed phosphor treatment and a clean pixel display.

## Language support

ZXBasic currently supports the core language needed by sizeable Spectrum programs:

- Numeric and string variables, expressions, slicing, and standard math/string functions.
- `LET`, `IF`/`THEN`, `FOR`/`NEXT`, `GO TO`, `GO SUB`, `RETURN`, `STOP`, and `PAUSE`.
- Numeric and string arrays through `DIM`, plus `ERASE`.
- `DEF FN`, `DATA`, `READ`, `RESTORE`, `INPUT`, and `INPUT LINE`.
- `PRINT`, `AT`, `TAB`, print zones, scrolling, and embedded color controls.
- `PLOT`, `DRAW`, curved `DRAW`, `CIRCLE`, `POINT`, `ATTR`, and `SCREEN$`.
- `INK`, `PAPER`, `BRIGHT`, `FLASH`, `INVERSE`, `OVER`, `BORDER`, and `CLS`.
- `PEEK`, `POKE`, `CLEAR`, `RANDOMIZE`, `RND`, `INKEY$`, `REM`, and silent `BEEP`.

Display memory follows the Spectrum layout: addresses `16384`–`22527` expose its non-linear bitmap rows, and `22528`–`23295` expose the 32×24 color attribute map.

## Spectrum ROM

ZXBasic extracts the original 8×8 character set at runtime from a ZX Spectrum 48K ROM supplied by the user. The ROM is not included in this repository.

Either:

- Set `ZXBASIC_ROM_PATH` to the ROM file.
- Place a file named `48.rom` beside the application.

For local development, ZXBasic also checks for the standard ROM in a neighboring ZXSpeculator checkout.

## Build from source

ZXBasic requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0). It uses [Avalonia](https://avaloniaui.net/) for its cross-platform desktop interface.

```text
git clone https://github.com/deanthecoder/ZXBasic.git
cd ZXBasic
dotnet build ZXBasic.sln
dotnet test ZXBasic.sln
dotnet run --project src/ZXBasic/ZXBasic.csproj
```

## Compatibility

ZXBasic interprets BASIC directly; it is not a machine emulator. Z80 machine code and `RANDOMIZE USR` are outside its scope. Hardware-specific `IN`, `OUT`, printer commands, and tape commands are not supported. `BEEP` accepts and observes the requested duration without producing sound.

Snapshot import supports uncompressed 48K `.sna` files and extracts their BASIC program. Other machine state and embedded machine-code routines are not executed.

## License

ZXBasic source code and the included example programs are available under the [MIT License](LICENSE). A separately supplied Spectrum ROM remains subject to its own terms.
