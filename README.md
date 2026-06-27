# TaskPlayer

Tiny media controls that sit right on your Windows taskbar. Previous, play/pause, next.

![Player](assets/player.png)
![Controls](assets/controls.png)
![Volume Popup](assets/volume.png)

## What's New (C# WPF Rewrite)
TaskPlayer has been completely rewritten from Rust to C# WPF to provide much deeper integration with Windows and a highly polished UI. 

### New Features:
- **Settings Window**: Comprehensive configuration options.
  - ![Settings Window](assets/settings.png)
- **Volume Slider Popup**: Hover over the player to quickly adjust the volume.
- **Auto-Hide Functionality**: Configurable delay to automatically hide the player when not in use.
- **Fluid Animations**: Smooth transitions and hover effects.
- **System Tray Integration**: Easily access settings or exit the app.
- **Launch on Startup**: Option to automatically start the app with Windows.

## Core Features
- Prev / Play-Pause / Next buttons on the taskbar
- Hooks into whatever's currently playing via Windows SMTC
- Optional auto-start with Windows
- Works on Windows 10 & 11
