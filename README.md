# TaskbarMiniPlayer

Tiny media controls that sit right on your Windows taskbar. Previous, play/pause, next — that's it.

Built in Rust with raw Win32 + WinRT, no frameworks, no runtime. Just a ~128KB exe.

## Features

- Prev / Play-Pause / Next buttons on the taskbar
- Hooks into whatever's currently playing via Windows SMTC
- System tray icon with settings
- Optional auto-start with Windows
- Works on Windows 10 & 11

## Build

```
cargo build --release
```

Grab the exe from `target/release/taskbar-mini-player.exe` and run it. Done.

## License

Do whatever you want with it.
