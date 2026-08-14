# 🎹 Roblox Auto Piano Player (Windows Desktop Application)

Professional, high-precision automated two-handed piano player for **Roblox Virtual Piano** (61-key layout: C2 to C7).

Built with **Python 3.13+, PySide6, Win32 `SendInput` (Hardware ScanCodes), High-precision Monotonic Timing, Floating In-Game HUD Overlay, and Multi-Format Score Import Engine**.

---

## 🌟 Key Features

1. **Multi-Format Score Support**:
   - **MIDI (`.mid`, `.midi`)**: Multi-track, multi-channel, tempo change tracking, note velocity, note duration.
   - **MusicXML (`.musicxml`, `.xml`, `.mxl`)**: Grand staff (Staff 1 = RH, Staff 2 = LH), chord ties, dynamic meters.
   - **Numbered Musical Notation (Jianpu / `.txt`)**: `1 2 3 4 5 6 7`, `#4`, `b3`, `1'`, `1,`, chords `[1 3 5]`, duration extensions `-`.
   - **Score Image / PDF (OMR)**: Optical Music Recognition pipeline with OpenCV preprocessing and Audiveris adapter.
2. **True Two-Handed Playback (RH / LH)**:
   - Separate Right Hand and Left Hand tracks.
   - Live hand toggling: Practice Right Hand only, Left Hand only, or Both hands simultaneously.
3. **Hardware Scancode `SendInput` Integration**:
   - Sends DirectInput keyboard scan codes to prevent in-game key dropping or anti-cheat blocking.
   - **Zero memory tampering or DLL injection**: 100% safe external simulation.
4. **Mixed Shift & Chord Handling**:
   - Intelligent separation of unshifted white keys and Shift-modified black keys within chords.
   - **Micro-Arpeggio Conflict Resolution**: Automatically handles same physical key collisions (e.g. `q` [F3] and `Q` [Shift+q = F#3]).
5. **Floating In-Game HUD Overlay**:
   - Always-on-top, frameless, semi-transparent HUD showing song progress, countdown, BPM, and speed.
   - Compact and Expanded mode toggles.
   - Optional click-through mode (`WS_EX_TRANSPARENT`).
6. **Global Hotkeys**:
   - `F6`: Start Playback (with customizable 3s countdown)
   - `F7`: Pause / Resume
   - `F8`: Emergency Immediate Stop (instantly releases all keys and Shift)
   - `F4`: Show / Hide Floating HUD Overlay
7. **Roblox Target Window Focus Safety**:
   - Monitors active foreground window. Automatically pauses if Roblox loses focus to prevent accidental typing in other windows.
8. **Interactive 61-Key Visualizer & Piano Roll**:
   - Real-time key illumination matching the exact Roblox Virtual Piano layout.
   - Pitch Range analyzer with one-click **Octave Fit** for notes outside C2~C7.

---

## 🎹 Roblox 61-Key Keyboard Mapping (C2 ~ C7)

| Octave | Note | White Key (Natural) | Black Key (Sharp / Shift) |
| :--- | :--- | :--- | :--- |
| **Octave 1** | C2 ~ B2 | `1` `2` `3` `4` `5` `6` `7` | `!` `@` *(none)* `$` `%` `^` *(none)* |
| **Octave 2** | C3 ~ B3 | `8` `9` `0` `q` `w` `e` `r` | `*` `(` *(none)* `Q` `W` `E` *(none)* |
| **Octave 3** (Middle C) | C4 ~ B4 | `t` `y` `u` `i` `o` `p` `a` | `T` `Y` *(none)* `I` `O` `P` *(none)* |
| **Octave 4** | C5 ~ B5 | `s` `d` `f` `g` `h` `j` `k` | `S` `D` *(none)* `G` `H` `J` *(none)* |
| **Octave 5** | C6 ~ B6 | `l` `z` `x` `c` `v` `b` `n` | `L` `Z` *(none)* `C` `V` `B` *(none)* |
| **Highest** | C7 | `m` | - |

*(Configuration profile saved in `src/piano/profiles/roblox_virtual_piano_61.json`)*

---

## 🚀 Quick Start Guide

### 1. Run the Application
Double-click `start.bat` or run in terminal:
```bash
.\.venv\Scripts\python.exe run.py
```

### 2. How to Play in Roblox
1. Drag and drop any `.mid`, `.musicxml`, or `.txt` score onto the application window (or click **"Load Demo Sample"**).
2. Check the detected notes, BPM, and range. (Click **"Octave Fit"** if notes exceed the 61-key range).
3. Open Roblox and sit in front of the Virtual Piano.
4. Press **`F6`** on your keyboard.
5. The HUD overlay will count down: **`3.. 2.. 1.. PLAY`** and automatically perform the song with perfect rhythm and two-handed chords!
6. Press **`F7`** to pause/resume, or **`F8`** to stop at any time.

---

## 🧪 Automated Testing

To run the complete automated test suite (21 unit tests covering mappers, importers, timeline, mixed chords, key safety, scheduler, and GUI):
```bash
.\.venv\Scripts\python.exe -m pytest tests/
```

---

## 📦 Packaging to Standalone `.exe` (Optional)

To compile into a standalone Windows executable using PyInstaller:
```bash
.\.venv\Scripts\uv pip install pyinstaller
.\.venv\Scripts\pyinstaller --name "RobloxPianoPlayer" --windowed --add-data "src/piano/profiles;src/piano/profiles" run.py
```
The compiled executable will be located in `dist/RobloxPianoPlayer/`.
