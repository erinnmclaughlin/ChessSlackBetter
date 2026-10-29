# ChessSlackBetter

Play chess against other people in Slack threads. A C# / .NET 10 rebuild of
[chessbot](https://www.chris-saylor.com/chessbot/).

- `@ChessBetterBot challenge @someone` posts a challenge card; each game gets its own thread
- Board images after every move (from the mover's point of view), with last move and check highlighted
- Moves in long (`e2e4`, `e1g1`, `c7c8q`) or standard notation (`e4`, `Nf3`, `O-O`, `exd8=Q`)
- Type moves in the thread without mentioning the bot, if you like
- Turn notifications, resign / draw offers / abort (buttons or commands)
- Full rules: castling, en passant, promotion, checkmate, stalemate, threefold repetition, fifty-move rule, insufficient material
- Finished games are imported to lichess for computer analysis; PGN export any time
- `@ChessBetterBot stats`, `@ChessBetterBot leaderboard`, `@ChessBetterBot games`

## Commands

| Where | Command | |
|---|---|---|
| Anywhere | `@ChessBetterBot challenge @user [white\|black]` | Challenge someone (random colours by default) |
| Game thread | `@ChessBetterBot accept` / `decline` | Answer a challenge (or use the buttons). The challenger can `decline` to withdraw |
| Game thread | `@ChessBetterBot e2e4`, or just `e4` | Make a move |
| Game thread | `@ChessBetterBot resign` | Resign |
| Game thread | `@ChessBetterBot draw` / `decline draw` | Offer, accept or decline a draw |
| Game thread | `@ChessBetterBot abort` | Cancel before both players have moved (not counted in stats) |
| Game thread | `@ChessBetterBot board` / `pgn` | Re-post the board / export the game |
| Anywhere | `@ChessBetterBot games` | Your open games, with links |
| Anywhere | `@ChessBetterBot stats [@user]` / `leaderboard` | Results |
| Anywhere | `@ChessBetterBot help` | Help |

## Project layout

```
src/ChessSlackBetter.Chess   Rules engine: move generation, SAN/LAN, FEN, PGN, game end detection
src/ChessSlackBetter.Bot     ASP.NET Core app: Slack events + interactivity, SQLite storage, board PNGs
tests/                       xUnit tests (perft-verified move generation, bot flows with a fake Slack)
```

## Running it

You need a public HTTPS URL that Slack can reach — both for the event/interaction webhooks and so Slack
can fetch the board images. For local play, [ngrok](https://ngrok.com/) works: `ngrok http 5216`.

1. **Create the Slack app.** At https://api.slack.com/apps choose *Create New App → From an app manifest*,
   paste [`slack-app-manifest.yml`](slack-app-manifest.yml) and replace `YOUR-PUBLIC-URL`. Install it to the
   workspace.
2. **Configure secrets.** From the app settings, copy the *Bot User OAuth Token* (`xoxb-…`) and the
   *Signing Secret*:

   ```sh
   cd src/ChessSlackBetter.Bot
   dotnet user-secrets init
   dotnet user-secrets set Slack:BotToken xoxb-...
   dotnet user-secrets set Slack:SigningSecret ...
   dotnet user-secrets set ChessBot:PublicBaseUrl https://your-subdomain.ngrok-free.app
   ```

   (Environment variables work too: `Slack__BotToken`, `Slack__SigningSecret`, `ChessBot__PublicBaseUrl`.)
3. **Run.** `dotnet run --project src/ChessSlackBetter.Bot` (listens on http://localhost:5216).
   Slack will verify the event URL once the app is reachable — re-save the URLs in the app config if needed.
4. **Play.** Invite the bot to a channel (`/invite @ChessBetterBot`) and `@ChessBetterBot challenge @friend`.

If `PublicBaseUrl` is empty the bot still works but draws boards as text.

### Docker

```sh
docker build -t chessbot .
docker run -p 8080:8080 -v chessbot-data:/data \
  -e Slack__BotToken=xoxb-... -e Slack__SigningSecret=... \
  -e ChessBot__PublicBaseUrl=https://chess.example.com chessbot
```

### Settings

| Setting | Default | |
|---|---|---|
| `Slack:BotToken` | | Bot User OAuth Token |
| `Slack:SigningSecret` | | Used to verify requests come from Slack |
| `ChessBot:PublicBaseUrl` | | Public base URL for board images |
| `ChessBot:DatabasePath` | `chessbot.db` | SQLite file |
| `ChessBot:AllowMovesWithoutMention` | `true` | Accept bare moves like `e4` from players in a game thread |
| `ChessBot:ImportFinishedGamesToLichess` | `true` | Import finished games to lichess.org for an analysis link |

## Development

```sh
dotnet test
```

## Credits

Piece images are the "cburnett" set by Colin M.L. Burnett, licensed
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), as distributed by lichess.org.
