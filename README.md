<p align="center">
  <img alt="ASA Server Manager" src="https://img.shields.io/badge/ASA_Server_Manager-Neon_Control_Surface-00e5ff?style=for-the-badge&logo=windows-terminal&logoColor=06131a&labelColor=06131a" />
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-7df9ff?style=for-the-badge&logo=dotnet&logoColor=06131a&labelColor=06131a" />
  <img alt="Blazor" src="https://img.shields.io/badge/Blazor-Web_App-37f3ff?style=for-the-badge&logo=blazor&logoColor=06131a&labelColor=06131a" />
  <img alt="Tailwind v4" src="https://img.shields.io/badge/Tailwind-v4-00fff0?style=for-the-badge&logo=tailwindcss&logoColor=06131a&labelColor=06131a" />
  <img alt="ASA" src="https://img.shields.io/badge/ARK-Survival_Ascended-9dff00?style=for-the-badge&logoColor=06131a&labelColor=06131a" />
</p>

<h1 align="center">🦖 ASA Server Manager 🦕</h1>

<p align="center">
  Neon-styled web panel for running a single <b>ARK: Survival Ascended</b> server on Linux.
</p>

<p align="center">
  Proton. SteamCMD. Config files. Validation. systemd. RCON. Logs.
</p>

<p align="center">
  <b>Quick install:</b>
</p>

```bash
apt update && apt upgrade -y && apt install curl -y
bash -c "$(curl -fsSL https://raw.githubusercontent.com/DragoQC/ASA_Server_Manager/main/setup-server-webapp.sh)"
```

---

## 💠 What It Is

**ASA Server Manager** is a Blazor web app built to manage one ASA server with a cleaner workflow:

- install the web app
- prepare Proton and SteamCMD
- manage `asa.service`
- edit `asa.env`
- edit `Game.ini` and `GameUserSettings.ini`
- validate required runtime files
- start / stop the server
- send in-game commands through RCON

Think:

> glowing dino control panel + server tooling

---

## 🧬 Repo Layout

```text
ASA_Server_Manager/
├── serverwebapp/
│   ├── AsaServerManager.Web.csproj
│   ├── Components/
│   ├── Data/
│   ├── Models/
│   ├── Services/
│   ├── Styles/
│   ├── Templates/
│   └── wwwroot/
├── managerwebapp/
│   ├── Components/
│   ├── Properties/
│   ├── wwwroot/
│   └── managerwebapp.csproj
├── Utils/
│   ├── ClusterHelper.txt
│   ├── Game.ini
│   └── GameUserSettings.ini
├── setup-server-webapp.sh
├── setup-manager-webapp.sh
├── ASA_Server_Manager.sln
└── README.md
```

---

## 🌐 Web App

App root:

```bash
serverwebapp/
```

Stack:

- .NET 10
- Blazor Web App
- Interactive Server rendering
- Tailwind CSS v4 standalone CLI
- SQLite

Current pages:

- `Dashboard` -> host metrics
- `Install` -> Proton, SteamCMD, start script, service file
- `Validate` -> install readiness checks
- `Server Config` -> live `asa.env`
- `Game Config` -> `Game.ini` / `GameUserSettings.ini`
- `Game Shell` -> RCON console
- `Host Shell` -> Linux shell as `asa_web_app`
- `Logs` -> `asa.service` status view

---

## ⚡ Quick Start

Run locally:

```bash
cd serverwebapp
dotnet watch
```

Default URL:

```text
http://localhost:8000
```

---

## 🎨 Tailwind v4

Expected binary path:

```text
serverwebapp/tools/tailwindcss
```

Install it:

```bash
cd serverwebapp
mkdir -p tools
curl -fsSL -o tools/tailwindcss https://github.com/tailwindlabs/tailwindcss/releases/download/v4.2.2/tailwindcss-linux-x64
chmod +x tools/tailwindcss
```

`managerwebapp/` is a fresh Blazor scaffold reserved for the future global multi-server manager.

---

## 🚀 Server Setup

Installer script:

```text
setup-server-webapp.sh
```

Run from local clone:

```bash
chmod +x setup-server-webapp.sh
sudo bash setup-server-webapp.sh
```

Run direct from GitHub:

What it does:

- creates `asa_web_app`
- prepares `/opt/asa`
- installs Linux + .NET deps
- clones the repo
- publishes the web app
- creates and starts `asa-webapp.service`
- prepares the `asa.service` symlink
- grants the limited `systemctl` access the panel needs

---

## 📁 Utils

`Utils/` contains helper/reference files:

- `Utils/Game.ini`
- `Utils/GameUserSettings.ini`
- `Utils/ClusterHelper.txt`

These are not the live runtime files.

Live files are managed under `/opt/asa/...`.

---

## 📦 Runtime Paths

Important runtime locations:

- `/opt/asa/server/asa.env`
- `/opt/asa/proton/proton.env`
- `/opt/asa/start-asa.sh`
- `/opt/asa/systemd/asa.service`
- `/opt/asa/server/ShooterGame/Saved/Config/WindowsServer`

---

## 🛠️ Build

Release:

```bash
dotnet build serverwebapp/AsaServerManager.Web.csproj -c Release
```

Solution:

```bash
dotnet build ASA_Server_Manager.sln
```

---

## 🦴 Service Notes

- `asa-webapp.service` = the web panel
- `asa.service` = the ASA game server
- `Game Shell` = RCON, not bash
- `Host Shell` = Linux shell
- config files can always be updated later from the panel

---

## ✨ Theme

This project is intentionally aiming for:

- neon turquoise
- dark sci-fi control panels
- sharp operational tooling
- dinosaur energy

If it feels like a cyberpunk ARK terminal, good. 🦖
