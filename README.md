# TaskPlayer

Tiny media controls that sit right on your Windows taskbar. Previous, play/pause, next.

![Player](<img width="302" height="48" alt="Screenshot 2026-06-27 162810" src="https://github.com/user-attachments/assets/d5ae7289-28eb-47fd-8a53-0eb13c077cf2" />)
![TinyPlayer](<img width="121" height="40" alt="Screenshot 2026-06-27 162825" src="https://github.com/user-attachments/assets/81ed5b8d-3e2d-4809-8bfd-d02f590a3633" />)
![Volume Popup](<img width="393" height="112" alt="Screenshot 2026-06-27 162912" src="https://github.com/user-attachments/assets/73b2ccee-2206-4650-9793-f703ea4507db" />)

## What's New (C# WPF Rewrite)
TaskPlayer has been completely rewritten from Rust to C# WPF to provide much deeper integration with Windows and a highly polished UI. 

### New Features:
- **Settings Window**: Comprehensive configuration options.
  - ![Settings](<img width="679" height="499" alt="Screenshot 2026-06-27 162919" src="https://github.com/user-attachments/assets/8d82a08e-8454-43b4-8ef8-7b273e0d227d" />)
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
