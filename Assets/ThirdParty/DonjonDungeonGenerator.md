# Donjon dungeon generator attribution

The pure C# topology generator in `Assets/Scripts/DungeonGeneration` translates the logical
generation stages of `dungeon.pl`, the Random Dungeon Generator by drow:

- Source: https://donjon.bin.sh/code/dungeon/dungeon.pl
- Pinned source SHA-256: `e3bfc59480913cb47663b6b7deb2e0a375d648271badae84b44ddaea4709af60`
- Project: https://donjon.bin.sh/
- Original license: Creative Commons Attribution-NonCommercial 3.0 Unported
- License: https://creativecommons.org/licenses/by-nc/3.0/
- Retrieved: 2026-07-17

The translation covers initialization masks, packed/scattered room placement, room sills and
doors, biased recursive corridor tunneling, stair-end selection, and dead-end cleanup. It does
not copy or translate the original HTML, command-line presentation, GD imaging, palette, font,
or image-output code.

The original work is licensed for noncommercial use. Commercial distribution of this translated
generator requires permission compatible with that use from the original rightsholder; the rest
of this repository's licensing does not remove that requirement.
