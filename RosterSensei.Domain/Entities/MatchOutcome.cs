namespace RosterSensei.Domain.Entities;

// The API sends this as a string ("Win", "Loss" or "Draw") on every endpoint, or null for a
// match that isn't finished.
public enum MatchOutcome
{
    Win,
    Loss,
    Draw
}
