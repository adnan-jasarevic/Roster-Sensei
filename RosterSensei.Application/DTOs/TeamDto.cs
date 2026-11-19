namespace RosterSensei.Application.DTOs
{
    public class TeamDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        // "Captain", "Officer", "Player" or "Coach".
        public string CurrentUserRole { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
    }
}
