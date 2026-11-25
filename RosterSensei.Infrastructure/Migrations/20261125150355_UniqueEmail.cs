using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before the index can be unique, each email may only be used once. Where an email has more
            // than one account, keep the one that is used the most (owns the most teams, then is on the
            // most teams, then has played the most matches) and delete the rest. A deleted account's
            // teams, memberships, matches, invites and Steam link move to the kept account first, so the
            // only thing lost is the second login. The same email means the same person.
            migrationBuilder.Sql(DeleteDuplicateAccountsSql);

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");
        }

        private const string DeleteDuplicateAccountsSql = """
            DECLARE @Pairs TABLE (Seq int IDENTITY PRIMARY KEY, LoserId nvarchar(450) NOT NULL, KeeperId nvarchar(450) NOT NULL);

            WITH Ranked AS (
                SELECT u.Id, u.NormalizedEmail,
                       ROW_NUMBER() OVER (
                           PARTITION BY u.NormalizedEmail
                           ORDER BY (SELECT COUNT(*) FROM Teams t WHERE t.OwnerId = u.Id) DESC,
                                    (SELECT COUNT(*) FROM TeamMembers tm WHERE tm.UserId = u.Id) DESC,
                                    (SELECT COUNT(*) FROM MatchParticipation mp WHERE mp.UserId = u.Id) DESC,
                                    u.SteamVerified DESC,
                                    u.EmailConfirmed DESC,
                                    u.Id) AS Position
                FROM AspNetUsers u
                WHERE u.NormalizedEmail IS NOT NULL
            )
            INSERT INTO @Pairs (LoserId, KeeperId)
            SELECT loser.Id, keeper.Id
            FROM Ranked loser
            JOIN Ranked keeper ON keeper.NormalizedEmail = loser.NormalizedEmail AND keeper.Position = 1
            WHERE loser.Position > 1;

            DECLARE @Seq int = 1, @Count int = (SELECT COUNT(*) FROM @Pairs);
            DECLARE @Loser nvarchar(450), @Keeper nvarchar(450), @SteamId varchar(17), @SteamVerified bit;
            DECLARE @Moves TABLE (TeamId int NOT NULL, Role int NOT NULL, JoinedAt datetime2 NOT NULL);
            DECLARE @Shared TABLE (TeamId int NOT NULL, Role int NOT NULL);
            DECLARE @Ratings TABLE (TeamId int NOT NULL, MapName nvarchar(30) NOT NULL, Side int NOT NULL, Rating decimal(3,1) NOT NULL, UpdatedAt datetime2 NOT NULL);

            WHILE @Seq <= @Count
            BEGIN
                SELECT @Loser = LoserId, @Keeper = KeeperId FROM @Pairs WHERE Seq = @Seq;

                -- Teams the kept account isn't on yet: it takes the deleted account's place, role and map ratings.
                -- The old row goes first so the one-Captain-per-team index never sees two Captains.
                DELETE FROM @Moves;
                DELETE FROM @Ratings;
                DELETE FROM @Shared;

                INSERT INTO @Moves (TeamId, Role, JoinedAt)
                SELECT tm.TeamId, tm.Role, tm.JoinedAt
                FROM TeamMembers tm
                WHERE tm.UserId = @Loser
                  AND NOT EXISTS (SELECT 1 FROM TeamMembers k WHERE k.UserId = @Keeper AND k.TeamId = tm.TeamId);

                INSERT INTO @Ratings (TeamId, MapName, Side, Rating, UpdatedAt)
                SELECT r.TeamId, r.MapName, r.Side, r.Rating, r.UpdatedAt
                FROM MapRatings r
                JOIN @Moves m ON m.TeamId = r.TeamId
                WHERE r.UserId = @Loser;

                -- Teams both accounts are on: the kept account takes over a Captain role, or any role when it is
                -- only a Player there (Captain 1, Player 2, Officer 3, Coach 4).
                INSERT INTO @Shared (TeamId, Role)
                SELECT tm.TeamId, tm.Role
                FROM TeamMembers tm
                JOIN TeamMembers k ON k.UserId = @Keeper AND k.TeamId = tm.TeamId
                WHERE tm.UserId = @Loser AND tm.Role <> 2 AND (tm.Role = 1 OR k.Role = 2);

                DELETE FROM TeamMembers WHERE UserId = @Loser;

                UPDATE k SET Role = s.Role
                FROM TeamMembers k
                JOIN @Shared s ON s.TeamId = k.TeamId
                WHERE k.UserId = @Keeper;

                INSERT INTO TeamMembers (UserId, TeamId, Role, JoinedAt)
                SELECT @Keeper, TeamId, Role, JoinedAt FROM @Moves;

                INSERT INTO MapRatings (UserId, TeamId, MapName, Side, Rating, UpdatedAt)
                SELECT @Keeper, TeamId, MapName, Side, Rating, UpdatedAt FROM @Ratings;

                UPDATE Teams SET OwnerId = @Keeper WHERE OwnerId = @Loser;

                -- Match history, except matches where the kept account is already in the lineup.
                UPDATE mp SET UserId = @Keeper
                FROM MatchParticipation mp
                WHERE mp.UserId = @Loser
                  AND NOT EXISTS (SELECT 1 FROM MatchParticipation k WHERE k.UserId = @Keeper AND k.MatchId = mp.MatchId);

                -- Pending invites, except where the kept account already has one for that team.
                DELETE i FROM TeamInvites i
                WHERE i.TargetUserId = @Loser AND i.Status = 0
                  AND EXISTS (SELECT 1 FROM TeamInvites k WHERE k.TargetUserId = @Keeper AND k.TeamId = i.TeamId AND k.Status = 0);
                UPDATE TeamInvites SET TargetUserId = @Keeper WHERE TargetUserId = @Loser;
                UPDATE TeamInvites SET SenderUserId = @Keeper WHERE SenderUserId = @Loser;

                UPDATE TeamEvents SET CreatedByUserId = @Keeper WHERE CreatedByUserId = @Loser;
                UPDATE TeamOffDays SET CreatedByUserId = @Keeper WHERE CreatedByUserId = @Loser;

                -- The Steam link moves too when the kept account has none (a Steam account fits one user only).
                IF EXISTS (SELECT 1 FROM AspNetUsers WHERE Id = @Keeper AND SteamId IS NULL)
                BEGIN
                    SELECT @SteamId = SteamId, @SteamVerified = SteamVerified FROM AspNetUsers WHERE Id = @Loser;
                    IF @SteamId IS NOT NULL
                    BEGIN
                        UPDATE AspNetUsers SET SteamId = NULL WHERE Id = @Loser;
                        UPDATE AspNetUsers SET SteamId = @SteamId, SteamVerified = @SteamVerified WHERE Id = @Keeper;
                    END
                END

                -- Whatever is left (memberships the kept account already has, clashing lineup rows,
                -- roles, claims, tokens) goes with the account.
                DELETE FROM AspNetUsers WHERE Id = @Loser;

                SET @Seq += 1;
            END
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
