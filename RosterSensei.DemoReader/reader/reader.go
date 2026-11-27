// Package reader reads the end-of-match scoreboard of one map from a CS2 demo.
package reader

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"sort"
	"strconv"
	"strings"

	demoinfocs "github.com/markus-wa/demoinfocs-golang/v5/pkg/demoinfocs"
	common "github.com/markus-wa/demoinfocs-golang/v5/pkg/demoinfocs/common"
	events "github.com/markus-wa/demoinfocs-golang/v5/pkg/demoinfocs/events"
	msg "github.com/markus-wa/demoinfocs-golang/v5/pkg/demoinfocs/msg"
	st "github.com/markus-wa/demoinfocs-golang/v5/pkg/demoinfocs/sendtables"
)

const roundsPerHalf = 12

type Player struct {
	SteamID       string `json:"steamId"`
	Name          string `json:"name"`
	Kills         int    `json:"kills"`
	Deaths        int    `json:"deaths"`
	Assists       int    `json:"assists"`
	Damage        int    `json:"damage"`
	HeadshotKills int    `json:"headshotKills"`
}

type Coach struct {
	SteamID string `json:"steamId"`
	Name    string `json:"name"`
}

type Side struct {
	Score      int      `json:"score"`
	FirstHalf  int      `json:"firstHalf"`
	SecondHalf int      `json:"secondHalf"`
	Players    []Player `json:"players"`
	Coaches    []Coach  `json:"coaches"`
}

type Summary struct {
	PatchVersion *int   `json:"patchVersion"`
	Fingerprint  string `json:"fingerprint"`
	MapName      string `json:"mapName"`
	Rounds       int    `json:"rounds"`
	Sides        []Side `json:"sides"`
}

// Who was on which team, and each team's score, after a given number of rounds.
type snapshot struct {
	score   map[common.Team]int
	members map[uint64]common.Team
}

func intProp(e st.Entity, name string) int {
	if e == nil {
		return 0
	}
	v, ok := e.PropertyValue(name)
	if !ok || v.Any == nil {
		return 0
	}
	return v.Int()
}

// Read parses the demo from r. The fingerprint is the SHA-256 of exactly the bytes read, so pass the
// uncompressed demo. On failure, patch is still the demo's CS2 patch when its header could be read
// (0 otherwise), so a failure after a game update can be told apart from a broken file.
func Read(r io.Reader) (summary *Summary, patch int, err error) {
	hash := sha256.New()
	p := demoinfocs.NewParser(io.TeeReader(r, hash))
	defer p.Close()

	var mapName string
	p.RegisterNetMessageHandler(func(m *msg.CDemoFileHeader) {
		mapName = m.GetMapName()
		patch = int(m.GetPatchVersion())
	})

	snapshots := map[int]snapshot{}

	take := func() {
		gs := p.GameState()
		s := snapshot{score: map[common.Team]int{}, members: map[uint64]common.Team{}}
		for _, team := range []common.Team{common.TeamTerrorists, common.TeamCounterTerrorists} {
			ts := gs.Team(team)
			if ts == nil {
				continue
			}
			s.score[team] = ts.Score()
			for _, pl := range ts.Members() {
				if pl != nil && pl.SteamID64 != 0 {
					s.members[pl.SteamID64] = team
				}
			}
		}
		// A round restored from a backup is played again: the later snapshot replaces the earlier one.
		snapshots[gs.TotalRoundsPlayed()] = s
	}

	p.RegisterEventHandler(func(events.RoundEndOfficial) { take() })
	// The parser panics on data it doesn't understand, e.g. after a CS2 update.
	defer func() {
		if r := recover(); r != nil {
			summary, err = nil, fmt.Errorf("couldn't read the demo: %v", r)
		}
	}()

	if err = p.ParseToEnd(); err != nil && !errors.Is(err, demoinfocs.ErrUnexpectedEndOfDemo) {
		return nil, patch, err
	}
	take()

	gs := p.GameState()
	rounds := gs.TotalRoundsPlayed()
	if rounds == 0 {
		return nil, patch, errors.New("the demo has no played rounds")
	}

	final := snapshots[rounds]
	sides := map[common.Team]*Side{common.TeamTerrorists: {}, common.TeamCounterTerrorists: {}}
	for team, side := range sides {
		side.Score = final.score[team]
	}

	// The team a player ended on, mapped to the team entity they were on at a given point.
	teamAt := func(round int, finalTeam common.Team) common.Team {
		s, ok := snapshots[round]
		if !ok {
			return common.TeamUnassigned
		}
		for id, t := range final.members {
			if t == finalTeam {
				if then, ok := s.members[id]; ok {
					return then
				}
			}
		}
		return common.TeamUnassigned
	}
	for team, side := range sides {
		half := min(rounds, roundsPerHalf)
		if t := teamAt(half, team); t != common.TeamUnassigned {
			side.FirstHalf = snapshots[half].score[t]
		}
		if rounds > roundsPerHalf {
			end := min(rounds, 2*roundsPerHalf)
			if t := teamAt(end, team); t != common.TeamUnassigned {
				side.SecondHalf = snapshots[end].score[t] - side.FirstHalf
			}
		}
	}

	for _, pl := range gs.Participants().All() {
		if pl == nil || pl.SteamID64 == 0 || pl.IsBot {
			continue
		}
		team, ok := final.members[pl.SteamID64]
		if !ok {
			team = pl.Team
		}
		side, ok := sides[team]
		if !ok {
			continue
		}
		id := strconv.FormatUint(pl.SteamID64, 10)
		kills := intProp(pl.Entity, "m_pActionTrackingServices.m_iKills")
		damage := intProp(pl.Entity, "m_pActionTrackingServices.m_iDamage")
		name := pl.Name
		if v, ok := pl.Entity.PropertyValue("m_iszPlayerName"); ok && v.Any != nil {
			name = v.String()
		}
		// A coach is on a team but never alive (servers also tag them "COACH <name>"). Someone who
		// joined a team and never played is left out too.
		if intProp(pl.Entity, "m_pActionTrackingServices.m_iLiveTime") == 0 && kills == 0 && damage == 0 {
			if trimmed, isCoach := cutCoachTag(name); isCoach {
				side.Coaches = append(side.Coaches, Coach{SteamID: id, Name: trimmed})
			}
			continue
		}
		side.Players = append(side.Players, Player{
			SteamID:       id,
			Name:          name,
			Kills:         kills,
			Deaths:        intProp(pl.Entity, "m_pActionTrackingServices.m_iDeaths"),
			Assists:       intProp(pl.Entity, "m_pActionTrackingServices.m_iAssists"),
			Damage:        damage,
			HeadshotKills: intProp(pl.Entity, "m_pActionTrackingServices.m_iHeadShotKills"),
		})
	}

	// Drain whatever the parser didn't read, so the fingerprint covers the whole file.
	if _, err := io.Copy(io.Discard, r); err != nil {
		return nil, patch, err
	}

	out := &Summary{Fingerprint: hex.EncodeToString(hash.Sum(nil)), MapName: mapName, Rounds: rounds}
	if patch > 0 {
		v := patch
		out.PatchVersion = &v
	}
	for _, team := range []common.Team{common.TeamTerrorists, common.TeamCounterTerrorists} {
		side := sides[team]
		sort.Slice(side.Players, func(i, j int) bool { return side.Players[i].Kills > side.Players[j].Kills })
		if side.Players == nil {
			side.Players = []Player{}
		}
		if side.Coaches == nil {
			side.Coaches = []Coach{}
		}
		out.Sides = append(out.Sides, *side)
	}
	return out, patch, nil
}

// "COACH Kairi" becomes "Kairi".
func cutCoachTag(name string) (string, bool) {
	trimmed := strings.TrimSpace(name)
	if len(trimmed) < 5 || !strings.EqualFold(trimmed[:5], "COACH") {
		return trimmed, false
	}
	rest := strings.TrimLeft(trimmed[5:], " :-_|")
	if rest == "" {
		return trimmed, true
	}
	return rest, true
}
