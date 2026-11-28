using RosterSensei.Domain.Entities;
using RosterSensei.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RosterSensei.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Team> Teams { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<MatchMap> MatchMaps { get; set; }
    public DbSet<MatchCoach> MatchCoaches { get; set; }
    public DbSet<MatchDemoImport> MatchDemoImports { get; set; }
    public DbSet<DemoParserStatus> DemoParserStatuses { get; set; }
    public DbSet<TeamMember> TeamMembers { get; set; }
    public DbSet<TeamInvite> TeamInvites { get; set; }
    public DbSet<TeamEvent> TeamEvents { get; set; }
    public DbSet<TeamEventType> TeamEventTypes { get; set; }
    public DbSet<TeamOffDay> TeamOffDays { get; set; }
    public DbSet<MapRating> MapRatings { get; set; }
    public DbSet<SteamSignInState> SteamSignInStates { get; set; }
    public DbSet<UserAvailability> UserAvailabilities { get; set; }
    public DbSet<AvailabilityOverride> AvailabilityOverrides { get; set; }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All DateTime values are stored in UTC. Reading them back as DateTimeKind.Utc makes the API
        // serialize them with a trailing "Z", so the browser converts them to the user's local time.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // A Steam account can be linked to one user at most, even when two users link it at the same time.
        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.SteamId)
                .HasMaxLength(17)
                .IsUnicode(false);

            user.HasIndex(u => u.SteamId)
                .IsUnique()
                .HasFilter("[SteamId] IS NOT NULL");

            user.Property(u => u.SteamVerified).HasDefaultValue(false);

            user.Property(u => u.SteamPersonaName).HasMaxLength(64);
            user.Property(u => u.SteamAvatarUrl).HasMaxLength(512).IsUnicode(false);
            // The background refresh looks for linked accounts that are due.
            user.HasIndex(u => u.SteamProfileRefreshDueAt);

            // One account per email, even when two sign-ups with the same email arrive at the same time.
            // Identity's own check runs before the insert, so on its own it can't stop that race.
            user.HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
        });

        // Steam sign-ins waiting for the user to come back from Steam. Shared through the database, so a
        // sign-in survives an API restart and can finish on any API instance.
        builder.Entity<SteamSignInState>(state =>
        {
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).HasMaxLength(64).IsUnicode(false);
            state.Property(s => s.UserId).HasMaxLength(450);
            state.Property(s => s.ReturnTo).HasMaxLength(2048);
            state.HasIndex(s => s.UserId);
            state.HasIndex(s => s.ExpiresAt);
            state.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TeamMember>()
            .HasKey(tm => new { tm.UserId, tm.TeamId });

        // Plain index for the Team foreign key, kept alongside the filtered one below.
        builder.Entity<TeamMember>()
            .HasIndex(tm => tm.TeamId);

        // At most one Captain per team, enforced by the database as well as the service.
        builder.Entity<TeamMember>()
            .HasIndex(tm => tm.TeamId, "IX_TeamMembers_OneCaptainPerTeam")
            .IsUnique()
            .HasFilter("[Role] = 1");

        // At most one pending invite per user per team, even when two invites are sent at the same time.
        builder.Entity<TeamInvite>(invite =>
        {
            invite.Property(i => i.TargetUserId)
                .HasMaxLength(450);

            // Plain index for the Team foreign key, kept alongside the filtered one below.
            invite.HasIndex(i => i.TeamId);

            invite.HasIndex(i => new { i.TeamId, i.TargetUserId }, "IX_TeamInvites_OnePendingPerUser")
                .IsUnique()
                .HasFilter("[Status] = 0 AND [TargetUserId] IS NOT NULL");
        });

        builder.Entity<TeamMember>()
            .HasOne<AppUser>()
            .WithMany(u => u.TeamMemberships)
            .HasForeignKey(tm => tm.UserId);

        builder.Entity<TeamMember>()
            .HasOne(tm => tm.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(tm => tm.TeamId);

        // Participations point at the Identity user, the only user model.
        builder.Entity<MatchParticipation>(participation =>
        {
            participation.HasOne<AppUser>()
                .WithMany(u => u.MatchParticipations)
                .HasForeignKey(p => p.UserId);

            participation.Property(p => p.Role)
                .HasMaxLength(30);

            // 0.01 to 99.99.
            participation.Property(p => p.Rating)
                .HasPrecision(4, 2);

            // ADR up to 999.9, HS% up to 100.
            participation.Property(p => p.Adr)
                .HasPrecision(4, 1);
            participation.Property(p => p.HeadshotPercentage)
                .HasPrecision(4, 1);

            // A player is in a match's lineup at most once. Also the range that a lineup
            // replacement locks, so two replacements of the same lineup run one after the other.
            participation.HasIndex(p => new { p.MatchId, p.UserId })
                .IsUnique();
        });

        builder.Entity<Match>()
            .HasOne(m => m.Team)
            .WithMany(t => t.Matches)
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        // EF adds "AND Version = @original" to every UPDATE, so a concurrent edit makes the save fail.
        builder.Entity<Match>()
            .Property(m => m.Version)
            .IsConcurrencyToken();

        builder.Entity<TeamEvent>(e =>
        {
            e.HasOne(te => te.Team)
                .WithMany()
                .HasForeignKey(te => te.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(te => te.Title)
                .IsRequired()
                .HasMaxLength(100);

            e.Property(te => te.Description)
                .HasMaxLength(1000);

            e.Property(te => te.CreatedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            e.HasIndex(te => new { te.TeamId, te.StartTime });

            // A type can't be deleted while events still use it; the service moves them to "Other" first.
            e.HasOne(te => te.EventType)
                .WithMany()
                .HasForeignKey(te => te.EventTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // EF adds "AND Version = @original" to every UPDATE, so a concurrent edit makes the save fail.
            e.Property(te => te.Version)
                .IsConcurrencyToken();
        });

        builder.Entity<TeamEventType>(type =>
        {
            type.HasOne(t => t.Team)
                .WithMany(t => t.EventTypes)
                .HasForeignKey(t => t.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            type.Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(50);

            type.Property(t => t.Color)
                .IsRequired()
                .HasMaxLength(7);

            // Each premade type at most once per team, so a team has exactly one "Other" even when two
            // requests add a premade type at the same time.
            type.HasIndex(t => new { t.TeamId, t.Preset }, "IX_TeamEventTypes_OnePresetPerTeam")
                .IsUnique()
                .HasFilter("[Preset] IS NOT NULL");

            type.HasIndex(t => new { t.TeamId, t.Name })
                .IsUnique();
        });

        builder.Entity<TeamOffDay>(day =>
        {
            day.HasOne(d => d.Team)
                .WithMany()
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            day.Property(d => d.CreatedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            day.HasIndex(d => new { d.TeamId, d.Date })
                .IsUnique();
        });

        // Belongs to the user, so deleting the account takes it with them. Teams read it through their members.
        builder.Entity<UserAvailability>(availability =>
        {
            availability.HasKey(a => a.UserId);
            availability.Property(a => a.UserId).HasMaxLength(450);

            availability.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            availability.Property(a => a.TimeZone)
                .IsRequired()
                .HasMaxLength(AvailabilitySlots.MaxTimeZoneLength)
                .IsUnicode(false);

            availability.Property(a => a.WeeklySlots)
                .IsRequired()
                .HasMaxLength(AvailabilitySlots.PerWeek)
                .IsUnicode(false)
                .IsFixedLength();
        });

        builder.Entity<AvailabilityOverride>(day =>
        {
            day.HasOne(o => o.Availability)
                .WithMany(a => a.Overrides)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            day.Property(o => o.UserId).HasMaxLength(450);

            day.Property(o => o.Slots)
                .IsRequired()
                .HasMaxLength(AvailabilitySlots.PerDay)
                .IsUnicode(false)
                .IsFixedLength();

            // One override per user and date, even when two saves of the same date arrive at once.
            day.HasIndex(o => new { o.UserId, o.Date })
                .IsUnique();
        });

        builder.Entity<MapRating>(rating =>
        {
            // Belongs to the membership, not the team or the user directly: leaving, being removed and
            // deleting the team all take the member's ratings with them, and a rating can't be saved for
            // someone who isn't (or is no longer) on the team, even when they leave mid-request.
            rating.HasOne(r => r.Member)
                .WithMany()
                .HasForeignKey(r => new { r.UserId, r.TeamId })
                .OnDelete(DeleteBehavior.Cascade);

            rating.Property(r => r.UserId)
                .HasMaxLength(450);

            rating.Property(r => r.MapName)
                .IsRequired()
                .HasMaxLength(30);

            // 1.0 to 10.0.
            rating.Property(r => r.Rating)
                .HasPrecision(3, 1);

            // One rating per member, map and side, even when two saves of a first rating arrive at once.
            // Also covers the foreign key to the membership.
            rating.HasIndex(r => new { r.UserId, r.TeamId, r.MapName, r.Side })
                .IsUnique();

            // The tab loads the whole team's ratings at once.
            rating.HasIndex(r => r.TeamId);
        });

        builder.Entity<MatchMap>(map =>
        {
            map.HasOne(mm => mm.Match)
                .WithMany(m => m.Maps)
                .HasForeignKey(mm => mm.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            map.Property(mm => mm.MapName)
                .IsRequired()
                .HasMaxLength(50);

            map.HasIndex(mm => new { mm.MatchId, mm.Order })
                .IsUnique();
        });

        builder.Entity<MatchCoach>(coach =>
        {
            coach.HasOne(c => c.Match)
                .WithMany(m => m.Coaches)
                .HasForeignKey(c => c.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            coach.Property(c => c.UserId).HasMaxLength(450);
            coach.Property(c => c.Name).HasMaxLength(MatchCoachLimits.MaxNameLength);
            coach.Property(c => c.SteamId).HasMaxLength(20);

            // Not a foreign key: a coach who deletes their account stays in the match's history by name.
            coach.HasIndex(c => new { c.MatchId, c.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
            coach.HasIndex(c => new { c.MatchId, c.SteamId })
                .IsUnique()
                .HasFilter("[SteamId] IS NOT NULL");
        });

        builder.Entity<MatchDemoImport>(import =>
        {
            import.HasOne(i => i.Match)
                .WithMany(m => m.DemoImports)
                .HasForeignKey(i => i.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            import.Property(i => i.MapName).IsRequired().HasMaxLength(50);
            import.Property(i => i.Fingerprint).IsRequired().HasMaxLength(64);
            import.Property(i => i.ParserVersion).IsRequired().HasMaxLength(50);
            import.Property(i => i.ImportedByUserId).IsRequired().HasMaxLength(450);

            // One demo per map of a match.
            import.HasIndex(i => new { i.MatchId, i.MapOrder })
                .IsUnique();
            // The same demo can't be imported twice by one team, even by two people at once.
            import.HasIndex(i => new { i.TeamId, i.Fingerprint })
                .IsUnique();
            // For the update guard's "newest patch imported so far".
            import.HasIndex(i => i.PatchVersion);
        });

        builder.Entity<DemoPlayerStats>(stats =>
        {
            stats.HasOne(s => s.MatchDemoImport)
                .WithMany(i => i.Players)
                .HasForeignKey(s => s.MatchDemoImportId)
                .OnDelete(DeleteBehavior.Cascade);

            stats.Property(s => s.UserId).IsRequired().HasMaxLength(450);
            stats.Property(s => s.SteamId).IsRequired().HasMaxLength(20);

            stats.HasIndex(s => new { s.MatchDemoImportId, s.UserId })
                .IsUnique();
        });

        builder.Entity<DemoParserStatus>(status =>
        {
            status.HasKey(s => s.ParserVersion);
            status.Property(s => s.ParserVersion).HasMaxLength(50);
        });

        builder.Entity<Team>(team =>
        {
            team.Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(100);

            // Ownership lives on the team, so a team always has exactly one Owner.
            team.Property(t => t.OwnerId)
                .IsRequired()
                .HasMaxLength(450);

            team.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(t => t.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
