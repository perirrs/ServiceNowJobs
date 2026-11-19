# Running the bot all the time

The bot is one small program. It has to be running for anything to happen: asking, reading replies, chasing, summaries, the board. This page covers where it runs today and how to move it to a machine that is always on.

## Where it runs today: this Windows PC

Installed as a scheduled task called **MandSTeamsBot**, in `C:\Mergen\MandSTeamsBot-service`.

- Starts when you sign in to Windows, and restarts itself within about 15 seconds if it ever stops.
- Does not depend on any other app being open.
- Stops when the PC is off, asleep or offline, or when you are signed out. When it comes back after more than 10 minutes away it tells the admins in the bot chat how long it was offline.

| To do this | Run this, from the `MandSTeamsBot` source folder |
|---|---|
| Update after a code change | `npm run build`, then `powershell -ExecutionPolicy Bypass -File service\install-windows.ps1` |
| Stop it and remove the task | `powershell -ExecutionPolicy Bypass -File service\uninstall-windows.ps1` |
| See what it is doing | Open `C:\Mergen\MandSTeamsBot-service\data\bot.log` |

The service folder owns the settings (`.env`) and the data from now on. The copies in the source folder are no longer used.

To keep it going through the working day on this PC, set Windows not to sleep while plugged in (*Settings → System → Power → Screen and sleep*).

## Moving to an always-on machine

### What the machine needs

Very little. The bot only makes outbound connections (to Teams and to the Claude API), so it needs **no public address, no open ports and no Azure subscription**.

- 1 CPU, 1 GB of memory, 10 GB of disk.
- Linux with Docker, or Linux or Windows with Node.js 22.13 or newer.
- Outbound HTTPS to the internet.
- A disk that persists: the database and the sign-in token must survive restarts.

### Options

| Option | Rough cost | Good for | Watch out for |
|---|---|---|---|
| **Small cloud server (recommended)**: any provider's entry-level Linux VM | £4 to £8 a month | Runs regardless of power cuts or anyone's laptop. Easy to back up. | Someone has to own the account and keep the server patched. |
| **Spare office PC or mini PC** left on | Hardware you already have | No monthly cost, data stays in the office. | Power cuts, someone switching it off, Windows updates rebooting it. |
| **Azure** (VM or Container Apps) | From about £8 a month | Keeps everything with Microsoft, alongside your tenant. | You have no Azure subscription today, so one must be created first. |

A cloud server in India or the UK is the simplest of these. The bot's own timezone is a setting, so the server's location does not affect when people are asked.

### The move, step by step

Plan for about 30 minutes. Only one copy of the bot may ever run: two copies would send every message twice.

1. **Stop the bot on this PC.** Run `service\uninstall-windows.ps1`. Do this first.
2. **Copy three things to the new machine**, into one folder (for example `/opt/mands-teams-bot`):
   - the source code (clone the repository, `MandSTeamsBot` folder);
   - `.env` from `C:\Mergen\MandSTeamsBot-service`;
   - the `data` folder from `C:\Mergen\MandSTeamsBot-service`. This holds the database (everyone's notes and memory) and `graph-token.json` (your Teams sign-in).

   Use an encrypted copy (`scp`, or a provider's secure upload). Both `.env` and `data` are secrets.
3. **Start it.**
   - With Docker: `docker compose up -d --build`. On Linux, first run `sudo chown -R 1000:1000 data` so the container can write to it.
   - Without Docker: `npm ci && npm run build`, then install `deploy/mands-teams-bot.service` as described in that file.
4. **Check it.** The log should show `Check-in bot running`. In Teams, send `board` to the bot: it should answer, and say how long it was offline.
5. **Delete the copies left on this PC**: `C:\Mergen\MandSTeamsBot-service\data` and `.env`. The token there can read and send your Teams chats.

Nothing changes in Teams, Entra or the Developer Portal. The bot registration, the app and your sign-in all carry over.

### If the Teams sign-in is not accepted on the new machine

The token is tied to your account, not to the PC, so it normally just works. If the log says sign-in is missing or expired, run the sign-in once on the new machine and enter the code it prints at the address shown:

- Docker: `docker compose run --rm bot node dist/signin.js`
- Without Docker: `npm run signin`

### Looking after it

- **Back up the `data` folder** daily. It is one small folder. Losing it loses every note and the memory; the bot itself can be rebuilt from the repository.
- **Restrict access** to the machine and that folder. Anyone who can read `data/graph-token.json` can read and send your Teams chats; anyone who can read `.env` can use your Claude and bot credentials.
- **Updates**: pull the new code and run `docker compose up -d --build` (or rebuild and `systemctl restart mands-teams-bot`).
- **If it goes down**, the admins are told how long it was offline when it comes back. It does not alert anyone while it is down, so if a day passes with no summaries, check the machine.
- **Secrets to rotate if a machine is ever lost or shared**: the bot's client secret (Teams Developer Portal), the Anthropic API key (Claude Console), and your Teams sign-in (delete `graph-token.json` and remove the app's consent under *Enterprise applications* in Entra).

### Later, if this becomes permanent

Socket mode, which lets the bot run without a public address, is marked by Microsoft as for development. It works, but for long-term use the supported route is an Azure Bot registration with a public HTTPS endpoint (`SOCKET_MODE=false`). That needs an Azure subscription and a domain or hosted URL. It is also what a full web dashboard tab in the app would need.
