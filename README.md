# Roster Sensei

A web app for amateur Counter-Strike 2 teams to manage their roster, schedule matches and track results.

> Work in progress. See [Roadmap](#roadmap) for what's next.

## Features

- **Accounts:** register and log in with email and password (JWT authentication).
- **Teams:** create up to 3 teams you own and switch between all the teams you're in.
- **Roles:** every member is a Captain, Officer, Player or Coach. A team has one Captain and any number of Officers and Coaches, who have the same rights as the Captain: they invite players, record matches and manage the calendar. Coaches don't play, so they can't be put in a lineup and have no player statistics.
- **Ownership:** each team has one Owner, who also has a role. Whoever creates a team is its Owner and Captain. Only the Owner manages roles, removes players, transfers ownership (only to an Officer or a Coach) and deletes the team.
- **Leaving:** any member except the Owner can leave a team. The Owner transfers ownership or deletes the team first.
- **Invites:** invite registered users by email. They accept or decline from the notification bell.
- **Dashboard:** win rate, member count, recent results and upcoming matches for the selected team.
- **Matches:** schedule or record matches with opponent, tournament, format (Bo1/Bo2/Bo3/Bo5), maps in play order, score and notes. Filter by upcoming, needs result or results. Any match can be edited later: a scheduled match whose time has passed shows "Needs result" with an **Add result** button, and results can be corrected or a match cancelled. If someone else saved the match after you opened it, your save is refused instead of overwriting theirs.
- **Lineups:** the Owner, Captain, Officers and Coaches record who played each match from the Lineup dialog on the Matches page, with an optional free-text role and whether they played or sat on the bench. The match list shows each lineup.
- **Player ratings:** when adding a finished match, tick who played and optionally give each a rating from 0.01 to 99.99 (a comma works as the decimal point). Ratings can be added or corrected later in a match's lineup, only for players who played in a finished match.
- **Player stats per match:** the Lineup dialog of a finished match also takes each player's kills, deaths, assists, ADR and HS% for the whole match. Every value is optional.
- **Demo import:** a manager can import one map's CS2 demo (.dem, or a .gz, .bz2 or .zip holding one, up to 1 GB) into an existing match. The demo is read in the browser and never uploaded; only its scoreboard is sent. The map score and halves, each linked player's kills, deaths, assists, ADR and HS%, and the coaches are saved after a preview. Players are matched to the roster by their linked Steam account and can be linked or left out by hand. A series adds up the stats of its imported maps, and the same demo can only be imported once per team. Stats typed in by hand are replaced; ratings stay.
- **Coaches per match:** a match records which coaches were there, team members or people from outside the team, filled in from a demo or by hand.
- **Match details:** every member can open a match's details from the Matches page: result, maps, notes, and a scoreboard of the players who played (K, D, A, +/-, K/D, ADR, HS%, rating) with the team's totals.
- **Player statistics:** a section of the Statistics tab with each player's matches played, win rate, K/D, average ADR and HS%, and their rating average, form (last 5 rated matches), best and worst. Filters: match type (the tab's switch), period, map and a minimum number of matches played. Columns sort; a player without a value goes last.
- **Calendar:** plan praccs, official matches, VOD reviews and more in month, week, day and agenda views.
  - **Event types:** each team picks its own. New teams start with Pracc, Official Match, VOD Review, Theory and Other; Scrim, Recorded Match, Break, Warmup and Meet can be added, as can custom types. Types can be renamed, recoloured and removed. Removing a type keeps its events and moves them to Other, which every team always has and which can't be changed.
  - **Recorded matches:** with the Recorded Match type on, matches from the Matches page show on the calendar, and one that starts during a Scrim or Official Match event takes that event's place.
  - **Off days:** a day marked off is grayed out and no events can be added to it. Events already on it show as cancelled and come back if the day is unmarked.
- **Availability (When2Play):** each member paints when they can play, When2Meet style, in half hours. A usual week repeats every week, and any date can be changed on its own and reset later. Times are saved in the member's time zone, so they stay put across daylight saving changes and teammates abroad see them at the right moment. The Team view is a heatmap of how many players are free, with who is free at each half hour and the week's lineup windows. Coaches can fill theirs in but don't count.
  - **On the calendar:** whenever 5 players are free together, week and day views shade that time and month view tags the day. Off days show none. The Owner, Captain, Officers and Coaches can click a month tag to plan an event in that window.
- **Steam:** link your Steam account on the Account page by signing in through Steam (OpenID), which proves the account is yours; Roster Sensei never sees the Steam password. The Team roster shows each member's Steam name and avatar and links to their profile. One Steam account can be linked to one user. Links pasted before Steam sign-in existed show as Unverified until the user signs in through Steam.

## Tech stack

| Layer | Technology |
| --- | --- |
| Backend | ASP.NET Core 10 Web API, Entity Framework Core 10, SQL Server |
| Auth | ASP.NET Core Identity, JWT bearer tokens |
| Validation | FluentValidation, applied by a global action filter |
| Frontend | React 19, TypeScript, Vite, Tailwind CSS 4, React Router 7, Axios |

## Project structure

The backend follows Clean Architecture: dependencies point inward, so the domain knows nothing about the database or the web.

Everything with a lifecycle (invites, roles and ownership, match status, the session, loading and saving in the UI) is listed with its states and rules in [docs/state-machines.md](docs/state-machines.md).

```
RosterSensei.Domain            Entities and enums (Team, TeamMember, TeamInvite, Match, ...)
RosterSensei.Application       DTOs, services, validators, repository interfaces
RosterSensei.Infrastructure    EF Core DbContext, migrations, repositories, Identity
RosterSensei.Api               Controllers, validation filter, exception handler, startup
RosterSensei.Client            React frontend
RosterSensei.Tests             xUnit tests for the services and the HTTP API, run against an in-memory SQLite database
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20 or newer
- SQL Server. The default connection string uses LocalDB (`(localdb)\mssqllocaldb`), which ships with Visual Studio on Windows. On macOS or Linux, run SQL Server in Docker and override the connection string (see below).
- The EF Core CLI, for applying migrations: `dotnet tool install --global dotnet-ef`

### 1. Configure the API

The JWT signing key is not committed. Set one of at least 32 characters with user secrets:

```bash
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project RosterSensei.Api
```

To use a database other than LocalDB, override the connection string the same way:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your connection string>" --project RosterSensei.Api
```

The API only accepts browser requests from origins listed under `Cors:AllowedOrigins`. In Development that's `http://localhost:5173`, the Vite dev server (see `appsettings.Development.json`). Steam sign-in only returns users to these origins too, so add your production frontend there.

Steam names and avatars on the roster come from public Steam Community profiles, so no Steam Web API key is needed. They're stored with each user: looked up right after a Steam sign-in, then refreshed once a week by a background job in the API (it checks every 15 minutes for accounts that are due). Rosters never wait on Steam. If Steam is down or rate-limits the API, the old name and avatar stay and the job tries that account again a day later. A player's display name is always the one they set; the Steam name is shown next to it on the roster.

Steam sign-ins in progress are stored in the database (table `SteamSignInStates`, 10 minutes each, expired ones deleted automatically), so a sign-in survives an API restart and works with more than one API instance.

### 2. Create the database

Restore the NuGet packages first, because `dotnet ef` fails on a fresh clone without them:

```bash
dotnet restore
dotnet ef database update --project RosterSensei.Infrastructure --startup-project RosterSensei.Api
```

### 3. Run the API

```bash
dotnet run --project RosterSensei.Api --launch-profile https
```

The API listens on `https://localhost:7185`, and Swagger UI is at `/swagger` in Development. In Visual Studio, set `RosterSensei.Api` as the startup project and press F5.

### 4. Run the frontend

```bash
cd RosterSensei.Client
npm install
npm run dev
```

Open http://localhost:5173. The frontend calls `https://localhost:7185/api` by default. To point it elsewhere, copy `.env.example` to `.env.local` and set `VITE_API_BASE_URL`.

### Master tester tools (development only)

A master tester account gets a **Tester** button in the corner of every page. It creates a full team in one click (made-up players, matches with maps and lineups, calendar events, off days and map pool ratings), fills any single section of the selected team, and sends test invites. Each section can be filled with **Add**, which keeps what's there, or **Replace**, which clears it first.

The tools are only switched on in Development (`MasterTester:Enabled` in `appsettings.Development.json`) and always off in Production. Testers are listed by email in user-secrets, never in the code:

```bash
dotnet user-secrets set "MasterTester:Emails:0" "<tester account email>" --project RosterSensei.Api
```

For everyone else, the endpoints under `/api/master-tester` answer 404. Made-up players use `@rostersensei.test` emails and have no password, so nobody can log in as them.

## Tests and CI

The backend tests run the real services and repositories against an in-memory SQLite database, so they need no SQL Server:

```bash
dotnet test RosterSensei.Tests
```

They cover team membership, invites, roles and permissions, ownership transfer, leaving and deleting teams, Steam sign-in and account linking, and match lineups. The tests in `RosterSensei.Tests/Api` also start the whole API in memory and call it over HTTP, to check sign-up, login, JWT validation, request validation and the status codes errors map to.

The frontend has browser tests (Playwright and axe) that run the production build against a mocked API, so they need no backend either:

```bash
cd RosterSensei.Client
npx playwright install chromium   # once
npm run test:e2e
```

They cover logging in, modal and menu focus, switching teams, loading, empty and error states, what each role can do, the Add Match request, and accessibility scans in both themes.

GitHub Actions runs on every pull request and on every push to `master`: it builds the API and runs the tests, rebuilds the demo reader and checks it matches the committed file, then lints and builds the frontend and runs its browser tests.

### Demo reader

Demos are read in the browser by `RosterSensei.DemoReader`, a small Go program built on [demoinfocs-golang](https://github.com/markus-wa/demoinfocs-golang) and compiled to WebAssembly. It reads the file piece by piece in a Web Worker, so even a big demo uses little memory. The build lives in `RosterSensei.Client/public/demo-reader`; after changing the Go code or updating demoinfocs-golang, rebuild it (needs the Go version in `go.mod`) and bump `DEMO_PARSER_VERSION` in `src/lib/demo-reader.ts`:

```bash
./RosterSensei.DemoReader/build.sh
cd RosterSensei.DemoReader && go run ./cmd/native path/to/demo.dem   # prints what a demo holds
```

When a demo from a newer CS2 patch than any imported so far can't be read, the API pauses demo import for that reader version, since a game update has most likely changed the demo format. A release with a new `DEMO_PARSER_VERSION` starts unpaused. Stats can still be typed in by hand meanwhile.

## Roadmap

Planned features:

- Keeping the last 10 imported demos so they can be downloaded again, once hosting is decided
