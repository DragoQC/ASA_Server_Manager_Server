<p align="center">
  <img alt="ASA Server Node API Banner" src="https://capsule-render.vercel.app/api?type=waving&height=220&color=0:05070d,35:083344,68:06b6d4,100:164e63&text=ASA%20Server%20Node%20API&fontColor=ffffff&fontSize=34&fontAlignY=38&desc=Neon%20web%20panel%20for%20one%20ARK%3A%20Survival%20Ascended%20server%20node&descAlignY=58&animation=twinkling" />
</p>

<p align="center">
  <img alt="ASA Server Node API" src="https://img.shields.io/badge/ASA_Server_Node_API-Server_Node-a5f3fc?style=for-the-badge&logo=windows-terminal&logoColor=05070d&labelColor=083344" />
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10_RC-cffafe?style=for-the-badge&logo=dotnet&logoColor=05070d&labelColor=083344" />
  <img alt="Blazor" src="https://img.shields.io/badge/Blazor-Interactive_Server-67e8f9?style=for-the-badge&logo=blazor&logoColor=05070d&labelColor=083344" />
  <img alt="SQLite" src="https://img.shields.io/badge/SQLite-Ready-a5f3fc?style=for-the-badge&logo=sqlite&logoColor=05070d&labelColor=083344" />
  <img alt="ARK ASA" src="https://img.shields.io/badge/ARK-Survival_Ascended-22d3ee?style=for-the-badge&logoColor=05070d&labelColor=083344" />
</p>

<h1 align="center">ASA Server Node API</h1>

<p align="center">
  Neon-style web panel for running one ARK: Survival Ascended Linux server fast.
</p>

<p align="center">
  One command -> working web panel in seconds -> clean single-node ASA management.
</p>

## One-line install

```bash
apt update && apt upgrade -y && apt install curl -y
bash -c "$(curl -fsSL https://raw.githubusercontent.com/DragoQC/asa_server_node_api/main/setup-server-webapp.sh)"
```

After install:

- web app: `http://<server-ip>:8000`
- default login: `admin / admin`
- first step: change password in `/admin/settings`

## 🧭 Project philosophy

- This tool is designed with strict constraints:

- ⚡ fast to deploy
- 🧩 simple to configure
- 💾 low footprint (disk + dependencies)
- 🎨 visually clean (neon UI)

Design decisions follow this:

avoid unnecessary complexity
keep everything local (no external dependencies required)

## What this repo is

This repo contains the server-side node for asa_server_node_api.

- 1 node = 1 ASA server
- Blazor-based web UI
- Full local management (no external services required)

Features
- ⚙️ install workspace (Proton, SteamCMD, scripts, services)
- 📄 live config editors:
- asa.env
- Game.ini
- GameUserSettings.ini
- 🔁 systemd integration:
- start / stop / restart
- 📡 RCON panel (commands executed automatically via UI/API)
- 📊 live host metrics
- 📜 logs + service state
- 🔐 API key authentication
- 🌐 public + admin API
- 🔄 SignalR real-time updates

## Product direction

The center point stays simple:

- paste one command
- get a working server manager fast
- expose an API on each server node
- use those APIs later for easy server-to-server / manager-to-server calls

Today this repo already exposes a small per-server API. Multi-server orchestration is the next layer, not the current claim.

## Stack

- `.NET 10.0`
- Blazor Web App
- Interactive Server components
- ASP.NET Core controllers + SignalR
- SQLite
- systemd

Project file: [asa_server_node_api/asa_server_node_api.csproj](/home/drago/Git/asa_server_node_api/asa_server_node_api/asa_server_node_api.csproj)

## What the installer does

Script: [setup-server-webapp.sh](/home/drago/Git/asa_server_node_api/setup-server-webapp.sh)

It will:

- install Linux deps
- install latest `.NET SDK 10.0`
- create user `asa_web_app`
- prepare `/opt/asa`
- clone `DragoQC/asa_server_node_api`
- publish the web app
- create and start `asa-webapp.service`
- prepare `/opt/asa/systemd/asa.service`
- symlink `/etc/systemd/system/asa.service`
- grant limited sudo/systemctl access needed by the panel

It does not auto-start the game server. It prepares the files and web app first.

## Runtime paths

- web app publish: `/opt/asa/webapp/publish`
- app DB: `/opt/asa/webapp/publish/Data/asa-manager.db`
- server env: `/opt/asa/server/asa.env`
- proton env: `/opt/asa/proton/proton.env`
- start script: `/opt/asa/start-asa.sh`
- service template: `/opt/asa/systemd/asa.service`
- live systemd link: `/etc/systemd/system/asa.service`
- game config dir: `/opt/asa/server/ShooterGame/Saved/Config/WindowsServer`

## Web routes

Public:

- `/` public server page

Admin:

- `/admin/login`
- `/admin/dashboard`
- `/admin/install`
- `/admin/validate`
- `/admin/server-config`
- `/admin/game-config`
- `/admin/game-shell`
- `/admin/host-shell`
- `/admin/logs`
- `/admin/email`
- `/admin/settings`

## API

Current per-server API:

Public endpoints:

- `GET /api/state`
- `GET /api/mods`

Admin endpoints:

- `POST /api/admin/start`
- `POST /api/admin/stop`
- `POST /api/admin/rcon`

Admin API auth:

- header: `X-Api-Key`
- key is managed in `/admin/settings`

Example:

```bash
curl http://127.0.0.1:8000/api/state
```

```bash
curl http://127.0.0.1:8000/api/mods
```

```bash
curl -X POST http://127.0.0.1:8000/api/admin/start \
  -H "X-Api-Key: YOUR_KEY"
```

```bash
curl -X POST http://127.0.0.1:8000/api/admin/rcon \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: YOUR_KEY" \
  -d '{"command":"ListPlayers"}'
```

SignalR hub:

- `/hubs/asa-state`

## Local dev

```bash
cd asa_server_node_api
dotnet watch
```

Default app URL:

```text
http://0.0.0.0:8000
```

SQLite connection string is in [asa_server_node_api/appsettings.json](/home/drago/Git/asa_server_node_api/asa_server_node_api/appsettings.json).

## Repo layout

```text
asa_server_node_api/
├── asa_server_node_api/
├── Utils/
├── setup-server-webapp.sh
├── asa_server_node_api.sln
├── Notes.md
└── README.md
```

## Utils

Reference files in [Utils](/home/drago/Git/asa_server_node_api/Utils):

- `Game.ini`
- `GameUserSettings.ini`
- `ClusterHelper.txt`

These are helper/reference files, not guaranteed live runtime files.
