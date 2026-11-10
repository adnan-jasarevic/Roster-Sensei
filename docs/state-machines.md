# State machines in Roster Sensei

Everything in the app that moves between a fixed set of states, in one list: what the states are,
what moves between them, and where that is enforced. Read this before changing any of them, and
update it when you do.

The rule behind it: **each machine has one typed home.** A status is a union or an enum, not a
free string. States that exclude each other are one value, not several flags. A rule that decides
a transition is written once and reused.

## On the server

The database is the last word on every transition. A check in a service gives the user a clear
message; the write itself repeats the check as a condition (`WHERE Status = Pending`), so two
requests at the same moment can't both win.

### Invite

`InviteStatus` in `RosterSensei.Domain/Entities/Enums/InviteStatus.cs`.

| From | To | When | Enforced in |
|---|---|---|---|
| (none) | Pending | A manager invites a registered user. One pending invite per team and email. | `TeamService.InviteMemberAsync`, unique index |
| Pending | Accepted | The invited user accepts. They join as a Player in the same transaction. | `TeamRepository.CompletePendingInviteAsync` |
| Pending | Declined | The invited user declines. | same |
| Pending | Canceled | The invited user accepts but is already a member. | `TeamService.RespondToInviteAsync` |

Accepted, Declined and Canceled are final. Tests: `InviteTests`.

### Team membership and ownership

A member has a role (`TeamRole`: Captain, Officer or Player). Ownership is separate: `Team.OwnerId`
points at one member, who also has a role.

| Transition | Who | Rule | Enforced in |
|---|---|---|---|
| Create team | Anyone | They become Owner and Captain. At most 3 owned teams. | `TeamService.CreateTeamAsync`, `TeamRepository.AddOwnedTeamAsync` |
| Join | Invited user | Joins as Player. | `CompletePendingInviteAsync` |
| Change role | Owner | Any role, their own too. Only one Captain per team. | `ChangeMemberRoleAsync`, unique index |
| Transfer ownership | Owner | Only to an Officer who owns fewer than 3 teams. Roles don't change. | `TransferOwnershipAsync` |
| Leave | Any member except the Owner | The Owner transfers or deletes first. | `LeaveTeamAsync`, `RemoveMemberAsync` |
| Kick | Owner | Not the Owner themselves. | `RemoveMemberAsync` |
| Delete team | Owner | Takes matches, events, members and invites with it. | `DeleteTeamAsync` |

"Can manage the team" (invites, matches, calendar) means Owner, Captain or Officer:
`TeamMember.CanManageTeam` on the server, `canManageTeam` in `src/types/index.ts` on the client.
The client also has `canReceiveOwnership` for the transfer rule. Tests: `TeamRoleTests`,
`RolePermissionTests`, `OwnershipTransferTests`, `LeaveAndDeleteTeamTests`, `ConcurrentChangeTests`,
`e2e/permissions.spec.ts`.

### Match

`MatchStatus` in `RosterSensei.Domain/Entities/Match.cs`: Scheduled, Live, Finished, Cancelled.

**There are no transitions.** A match is created in a status and stays in it: the API has no
endpoint that edits a match. What a status allows:

| Status | Scores | Counts in statistics | Shown as |
|---|---|---|---|
| Scheduled | None allowed (`CreateMatchDtoValidator`) | No | "Upcoming" while its time is ahead, "Needs result" once it has passed |
| Live | Allowed by the API. The form doesn't offer this status. | No | "Live", under All only |
| Finished | Series score, or worked out from map scores (`MatchService.SeriesScore`) | Yes | Result with outcome |
| Cancelled | Allowed by the API. The form sends none. | No | Struck through; hidden on the calendar |

On the client the status is the `MatchStatusName` union, and two functions name the questions
pages ask: `hasResult` (Finished), `isUpcoming` (Scheduled, time still ahead) and `needsResult`
(Scheduled, time passed). Requests send the status as a number (`MatchStatus` constant), responses
as a name.

"Upcoming" means the same thing everywhere: Scheduled with a time in the future. The dashboard
applies it on the server (`TeamService.GetTeamDashboardAsync`), the Matches page with `isUpcoming`.
A Scheduled match whose time has passed is "Needs result": it has its own filter and badge on the
Matches page. Tests: `e2e/matches.spec.ts`.

Known gaps, left alone because closing them changes what the product does:

1. A scheduled match can never become Finished or Cancelled. Recording its result means adding a
   second match.
2. "Needs result" can only be looked at for now: recording the result needs the transition above
   (issue #79).
3. The API sends `matchOutcome: "Draw"` and `finalScore: "0 - 0"` for a match that isn't finished.
   The client only shows them when `hasResult` is true.

### Calendar event

No status column. Two things are worked out on the client, in `items` in `src/pages/calendar.tsx`:

- **Cancelled:** the event overlaps an off day. It comes back when the day is unmarked.
- **Replaced:** a Scrim or Official Match event with a recorded match starting inside it is shown
  as that match.

Edits carry `Version`. A save over a newer version is refused with 409 (`TeamEventService.UpdateEventAsync`,
`TeamEventRepository.UpdateAsync`), and the calendar reloads so the user sees the newer one.
Tests: `EventTypeAndOffDayTests`, `ConcurrentChangeTests`, `e2e/calendar.spec.ts`.

### Event type, off day, Steam link

| Thing | States | Notes |
|---|---|---|
| Event type | Premade, custom, or the locked "Other" | Removing a type moves its events to Other. Other can't be changed or removed. `TeamEventTypeService` |
| Off day | Marked or not | Marking twice is not an error. Nothing can be planned on a marked day. `TeamOffDayService` |
| Steam link | Unlinked or linked | Unlinking twice is not an error. An ID linked to another user is a 409. `AccountService` |

### Results of a write

Services return `Result<T>` with an `ErrorType`, which `ApiControllerBase` turns into 400, 403, 404
or 409. Repositories report races as `WriteOutcome` (Done, Rejected, Conflict) or
`LineupWriteOutcome`. The client reacts to the status code, not the message.

## In the browser

### Session

`src/services/session.ts` is the only code that touches the stored token.

```
signed out --signIn--> signed in --signOut, or a 401 from the API--> signed out
```

`PrivateRoute` sends a signed-out user to `/login`. `SignedOutRoute` sends a signed-in user from
`/`, `/login` and `/register` to the dashboard. An expired token looks signed in until the first
API call returns 401; `src/services/api.ts` then signs out and loads the login page.
Tests: `e2e/auth.spec.ts`.

### Loading data: `useResource`

`src/hooks/use-resource.ts`. One hook derives the state from the latest settled request, so the
flags can't disagree:

| State | `isLoading` | `isFetching` | `data` | `error` | A page shows |
|---|---|---|---|---|---|
| Waiting (loader is `null`) | false | false | none | none | nothing |
| First load | true | true | none | none | skeleton |
| Ready | false | false | yes | none | content, or the empty state |
| Refreshing | false | true | yes | none | content, marked busy |
| Failed, nothing to show | false | false | none | yes | `ErrorState` with retry |
| Failed, older data kept | false | false | yes | yes | content with an `Alert` above it |

A response for a request the page has moved on from is aborted and ignored.
Tests: `e2e/page-states.spec.ts`, `e2e/team-switching.spec.ts`.

### One request started by the user: `useAction`

`src/hooks/use-action.ts`. Every save, delete, invite and login is one of these:

```
idle --run--> running --resolved--> idle
                      --rejected--> failed --run--> running
                                           --reset--> idle
```

A `run` while one is running is ignored, the old error is cleared when a new run starts, and
`running` always ends. Use it instead of an `isSaving` flag next to an `error` string. A dialog
with several actions (the event dialog has save, delete and "make it an off day") is busy while
any of them runs. Tests: `e2e/modals.spec.ts`, `e2e/calendar.spec.ts`.

Two places keep their own version on purpose, because they run several requests side by side:
the invite list in `top-navigation.tsx` (one per invite) and `event-types-modal.tsx` (one at a
time for the whole dialog, keyed by which button started it).

### Which dialog is open

A page with more than one dialog keeps a single union, so two can't be open at once and each
dialog carries its own data:

| Page | Type | Kinds |
|---|---|---|
| Calendar | `ModalState` | closed, create, open, offDay, types |
| Team roster | `Dialog` | none, invite, role, transfer, kick, leave, delete |
| Matches | `Dialog` | none, addMatch, lineup |

A dialog is mounted only while it is the open one, so it starts clean every time. Two flags that
are independent stay two flags: on the Account page the unlink confirmation opens over the change
form, and cancelling it leaves the form as it was.

### Team selection

`app-layout.tsx` loads the user's teams and keeps the selected id (also in `localStorage`).
`team-gate.tsx` turns that into what every team page sees:

```
loading -> failed (retry) | no team (create or accept an invite) | team selected
```

A remembered team the user is no longer in falls back to their first team. Pages render their
content with `key={team.id}`, so switching teams starts every machine on the page from scratch.

### Smaller ones

| Where | States |
|---|---|
| Dragged event (`PendingMove` in `calendar.tsx`) | saving, saved and waiting for the reload, dropped |
| Register page | form, submitting, registered (then redirects after 2 seconds) |
| Add match form | The status picks the fields: Finished shows a series score, or map scores once maps are picked. Best-of caps the maps. |
| Toast (`ui/toast.tsx`) | counting down, paused while hovered or focused; errors never count down |
| Modal (`ui/modal.tsx`) | closed, open, busy (every way of closing is blocked) |
