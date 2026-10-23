# Third-party components

The exe is compiled from this repository's sources and embeds two typefaces; everything else it uses is part of
Windows.

| Component | License | How it ships |
|---|---|---|
| Sora (text face; static instances of weights 450, 550, 600 and 650 of the variable latin subset, `src\fonts\Sora-W*.ttf`) | SIL Open Font License 1.1 (`src\fonts\LICENSE-Sora.txt`) | embedded in `MicControl.exe` as resources |
| Geist Mono (mono face; weight 450, cut to Basic Latin and Latin-1, `src\fonts\GeistMono-W450.ttf`) | SIL Open Font License 1.1 (`src\fonts\LICENSE-GeistMono.txt`) | embedded in `MicControl.exe` as resources |
| Microsoft .NET Framework 4.8 (runtime, WPF, Windows Forms, the C# compiler `csc.exe` used by the build) | Microsoft .NET Framework licence | part of Windows; not shipped |
| Windows Core Audio API (`IMMDeviceEnumerator`, `IAudioEndpointVolume`) | Windows licence | part of Windows; not shipped |

The font files are generated from the published variable fonts by `build\make-fonts.py` (fontTools); the OFL allows
embedding and modified versions under the same licence.
