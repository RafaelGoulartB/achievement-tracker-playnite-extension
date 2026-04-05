# 🏆 Achievement Tracker for Playnite

A powerful, integrated achievement tracking extension for [Playnite](https://playnite.link/). View your progress, track unlocks, and manage your library completion status all in one place.

![Main Screenshot Placeholder](https://via.placeholder.com/1200x600/1e1e1e/ffffff?text=Achievement+Tracker+Main+UI)

---

## ✨ Features

### 📊 Library Progress Sidebar
Get a bird's-eye view of your entire collection. The new sidebar panel shows your completion percentage across all installed games.
- **Dynamic Sorting:** Games are ranked by completion progress.
- **Visual Progress Bars:** Instant feedback on how close you are to the platinum.
- **Deep Integration:** Built directly into the Playnite sidebar for quick access.

### 🎮 Game Detail Integration
Track specific achievements directly on the game details page.
- **Real-time Scanning:** Automatically fetches achievements from Steam and local data files.
- **Beautiful UI:** Modern design with progress tracking and icon support.
- **Wide Compatibility:** Support for Steam, CODEX, RUNE, Goldberg (GSE), FLT, and more.

![Details Screenshot Placeholder](https://via.placeholder.com/800x400/1e1e1e/ffffff?text=Game+Details+View)

### 🔍 Supported Sources
| Source | Tracking Method |
| :--- | :--- |
| **Steam** | Steam Web API & HTML Scraper |
| **Goldberg (GSE)** | `achievements.json` & Local Stats |
| **CODEX / RUNE** | `.ini` file tracking |
| **FLT** | Local achievement file detection |

---

## 🚀 Installation

1. Download the latest `.pext` file from the [Releases](https://github.com/RafaelGoulartB/achievement-tracker-playnite-extension/releases) page.
2. Drag and drop the file into Playnite.
3. Restart Playnite when prompted.

### 🛠️ Manual Build (Developers)
If you want to build from source:
1. Clone the repository.
2. Run `make build` (requires `dotnet` SDK).
3. Copy the contents of the `bin` folder to your Playnite extensions directory.

---

## 🎨 Theme Support
Achievement Tracker is designed to be **theme-agnostic**. It uses transparent backgrounds and adaptive colors to match any Playnite theme, including **FusionX**, **DH_Darius**, and more.

---

## 🤝 Contributing
Contributions are welcome! If you have a suggestion or found a bug, feel free to open an issue or pull request.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---
