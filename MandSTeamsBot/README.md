# MandS Teams Bot: AI daily check-ins

A Microsoft Teams bot that talks to each person you nominate at the end of every working day, remembers what they said, and sends the CTO and CEO one digest showing who is stuck on what and what to ask them.

## What it does

1. **You nominate people and describe their job.** In a private chat with the bot, an admin sends `nominate priya@company.com`, then `profile priya@company.com: Senior recruiter on the ServiceNow desk. Targets: 10 submissions and 3 first interviews a week...`. Only the emails in `ADMIN_EMAILS` can do this.
2. **Every working day the bot messages each person** (5 pm by default). Questions are phrased from their role and targets, and from what is still open: if Priya said yesterday she was working on three submissions, today's opening asks what happened with those three submissions as well as what she did today.
3. **It follows up like a person would**, where an answer is vague or a blocker has no owner, and stops after a few exchanges (hard cap: `MAX_BOT_TURNS`).
4. **It writes a note for each person**: summary, progress against targets, blockers, pain points, help needed, and a list of tracked work items that stay open until the person says they are done.
5. **It sends leadership a digest** (7:30 pm by default): what needs attention first, a few lines per person, the exact questions to ask each person, and who gave no update.
6. **You can ask it things** at any time: "who has been blocked for more than two days?", "how is Ravi doing against his targets this fortnight?"

People who have not replied get one chase message after `CHASE_AFTER_MINUTES` (default 45). When a person's conversation finishes, or goes quiet for `IDLE_CLOSE_MINUTES` (default 30) after they have replied, their summary is posted to the admins straight away, ahead of the evening digest.

Messages to staff are kept to one short line in very simple English, with no names and no "thanks", so they read naturally for people whose first language is not English.

### Admin commands

| Command | Effect |
|---|---|
| `nominate name@company.com` | Start daily check-ins for someone |
| `profile name@company.com: ...` | Set role, responsibilities and targets (omit the text to view it) |
| `remove name@company.com` | Stop their check-ins; notes are kept |
| `list` | Everyone nominated and today's status |
| `report name@company.com` | Their last five notes and open items |
| `checkin name@company.com` / `checkin all` | Run a check-in now |
| `digest` | Produce today's digest now |
| anything else | A question answered from the last 14 days of notes |

## Try it before any Teams setup

You need Node.js 22.13 or newer and an Anthropic API key.

```bash
npm install
```

```bash
cp .env.example .env
```

Put your key in `.env` as `ANTHROPIC_API_KEY`, then:

```bash
npm run simulate
```

You play an employee in the terminal. Type `/nextday` to see the bot pick up yesterday's open items, `/digest` to see what leadership receives, and `/ask who is blocked?` to query the notes.

## Trial in Teams from your own machine (socket mode)

This needs no Azure subscription, public server or tunnel. The bot connects out to Teams over a WebSocket, so it only works while the process is running on your machine. Microsoft marks socket mode as for development only.

1. In the [Teams Developer Portal](https://dev.teams.microsoft.com/tools/bots), under *Tools → Bot management*, create a bot and set its delivery method to **Socket mode**. (For Mergen this is done: the bot is "Daily Check-in" and its ID is already in `appPackage/manifest.json`.)
2. On the bot's *Client secrets* page, add a secret and paste it into `.env` as `CLIENT_SECRET`. Also set `CLIENT_ID`, `TENANT_ID`, `SOCKET_MODE=true`, `ANTHROPIC_API_KEY` and `ADMIN_EMAILS`.
3. Zip the three files in `appPackage/` and upload the zip in the Teams admin centre under *Teams apps → Manage apps → Actions → Upload new app*. On the app's *Users and groups* tab, set availability to the specific people in the trial.
4. Run `npm run build` then `npm start`, and leave it running.
5. Each person in the trial opens Teams, finds **Daily Check-in** under *Apps → Built for your org*, adds it and says hello. Admins then `nominate` them.

## Sending check-ins from your own account

With `SEND_AS_USER=true`, check-ins arrive from a real person's Teams account, in that person's normal one-to-one chat with each colleague, instead of from the bot. Colleagues install nothing. The bot app is then only used by the admins, for commands and the digest.

How it behaves:

- On nomination, the colleague gets one message from your account saying the daily check-in is automated and sent by an AI assistant, and that answers are summarised for you and the CEO. The assistant also says so if asked.
- The assistant only reads and replies in a chat while that day's check-in is open. Before the opening question and after it closes, the chat is ordinary conversation and is left alone.
- If you type in a chat yourself during an open check-in, the assistant stays quiet there for the rest of the day. What the colleague says is still recorded for the note.
- Replies are picked up by polling (`POLL_SECONDS`, default 20), so expect a short delay.

With `DISCLOSE_AUTOMATION=false` the assistant instead writes as you in the first person and never mentions automation: no heads-up is sent on nomination. It does not claim to be human either. If a colleague asks whether the messages are automated, asks for a decision only you can give, or raises something personal, it stops replying in that chat and alerts you in the bot chat so that you answer yourself. Check what your local law and your staff policies require before using this setting; in some places people must be told when they are dealing with an AI system.

Setup:

1. In the Entra admin centre, open the bot's app registration → *Authentication* → set **Allow public client flows** to **Yes** and save. This permits the device-code sign-in below.
2. Run `npm run signin` as the person the messages should come from, and enter the printed code at the address shown. Approve the permissions requested: read and send your Teams chats (`Chat.ReadWrite`) and look up colleagues by email (`User.ReadBasic.All`).
3. Set `SEND_AS_USER=true` in `.env` and restart the bot.

The sign-in token is saved in `data/graph-token.json`. Anyone who can read that file can read and send that person's Teams chats, so keep the machine and folder locked down. To revoke it, delete the file and remove the app's consent under *Enterprise applications* in Entra.

## Putting it into Teams for real

You need to be (or work with) a Teams administrator for your Microsoft 365 tenant, and an Azure subscription.

1. **Host the bot** somewhere with a public HTTPS address and a disk that persists (an Azure App Service, a small VM, or any container host). For a first trial on your own machine, a dev tunnel works: `devtunnel host -p 3978 --allow-anonymous`.
2. **Register the bot in Azure.** Create an *Azure Bot* resource, type **Single Tenant**. This creates an app registration: note its **Application (client) ID** and **Directory (tenant) ID**, and create a **client secret** under *Certificates & secrets*.
3. In the Azure Bot's *Configuration*, set the **messaging endpoint** to `https://<your-host>/api/messages`. Under *Channels*, add **Microsoft Teams**.
4. **Fill in `.env`**: `CLIENT_ID`, `CLIENT_SECRET`, `TENANT_ID`, `ANTHROPIC_API_KEY`, `ADMIN_EMAILS` (the CTO's and CEO's Teams sign-in emails), `COMPANY_NAME`, `TIMEZONE`.
5. **Build and run:**

   ```bash
   npm run build
   ```

   ```bash
   npm start
   ```

6. **Package the Teams app.** In `appPackage/manifest.json`, replace both `REPLACE_WITH_CLIENT_ID` values with the client ID and fill in the developer name and URLs. Zip the three files in `appPackage/` (the files themselves, not the folder).
7. **Upload it** in the Teams admin centre under *Teams apps → Manage apps → Upload new app*.
8. **Install it for people.** Under *Setup policies*, add the app to *Installed apps* for everyone who will be nominated plus the two admins. Teams then installs it for them automatically, which is how the bot gets permission to message each person first.
9. **Both admins open the bot in Teams and say `help`.** The bot can only deliver the digest to an admin who has opened it once.

A person who is nominated before the app reaches them shows as "not connected" in `list`; the bot introduces itself as soon as the install lands or they message it.

## Things to know

- **Staff are told.** The bot's first message to each nominated person says their answers are summarised daily for the CTO and CEO, and it says so again if asked. Keep that in: people give better updates when they know where they go, and in the UK and EU you are required to tell staff about monitoring of this kind. Worth a line in your staff privacy notice too.
- **It records what people say, not how they seem.** Notes capture stated blockers and frustrations. The prompts tell the AI not to guess at mood or rate performance.
- **Data.** Everything lives in one SQLite file, `data/mands.db`: profiles, every chat message, every note and digest. Back it up, and restrict who can read the server. Conversation text is sent to Anthropic's API to generate replies and summaries.
- **Who can see what.** Admin rights come only from `ADMIN_EMAILS`, matched against the identity Teams vouches for. Nominated people can only ever see their own chat.
- **Cost.** Each person's check-in is a handful of short Claude calls, plus one larger call for the digest. The model is `claude-opus-5-5` by default; set `CLAUDE_MODEL` to change it.
- **Single instance.** The scheduler runs inside the process, so run exactly one copy.

## How it is put together

| File | Role |
|---|---|
| `src/index.ts` | Teams wiring and the three scheduled jobs |
| `src/engine.ts` | The logic: check-in conversation, reminders, digest, admin commands |
| `src/ai.ts` | Prompts and Claude calls (the place to tune how it asks and summarises) |
| `src/db.ts` | SQLite storage: people, check-ins, messages, notes, work items, digests |
| `src/simulate.ts` | Terminal simulator |
| `test/engine.test.ts` | Tests for the engine with a scripted stand-in for Claude |

```bash
npm test
```
