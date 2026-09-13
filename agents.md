# Agents.md — Guidance for AI-assisted Development

## Summary

ZXBasic is a cross-platform Sinclair BASIC environment written in C# and Avalonia. It recreates the Spectrum's programming experience without emulating the Z80 CPU.

## Project direction

- Keep the BASIC engine independent of Avalonia.
- Preserve the 256x192 display, 32x24 character layout, 8x8 ROM glyphs, Spectrum palette and color clash.
- Accept normal native-keyboard input and force BASIC source to uppercase for now.
- Keep execution interruptible and support both approximate Spectrum speed and accelerated modes.
- Implement and verify one coherent language area at a time.
- Do not support machine code, `RANDOMIZE USR`, printer hardware, `IN`, `OUT`, or Spectrum keyboard symbol modes.
- Accept `BEEP` eventually but produce no sound.

## Planned extensions

- Add `RENUM` or `RENUMBER`.
- Allow more program and variable memory than a physical 48K Spectrum.
- Consider optional higher-precision mathematics behind a compatibility setting.
- Add a safe command for reading string content from a host file.
- Add application menu commands for loading and saving listings.
- Allow clicking a listed line to edit it, and support arrow, Home, End and mouse-wheel navigation.
- Import tokenized BASIC programs from `.sna` snapshots.

## Structure

- `Basic` contains tokenisation, parsing, program storage and interpretation.
- `Emulation` contains the Spectrum-like screen, attributes, palette, memory and timing models.
- `Controls` adapts those models to Avalonia input and rendering.

## Coding preferences

- Match the American English and C# style used by ZXSpeculator.
- Use `var` where the type is obvious.
- Avoid single-line `if` statements.
- Use `string.Empty` instead of `""`.
- Use sentence-style comments with full stops.
- Keep methods small and focused.
- Use NUnit for focused behavioural tests.

## Assets and licensing

The MIT licence covers ZXBasic source code. Do not commit a Spectrum ROM or ROM-derived glyph data. Load glyphs from a ROM supplied by the user at runtime.
