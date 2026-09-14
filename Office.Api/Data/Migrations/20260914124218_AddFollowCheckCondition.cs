using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowCheckCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "follow_check_result",
                table: "automation_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Табдили шакли ҳамвори action_config_json-и Фазаи 10 ({CommentReplies, DmText,
            // DmButtonUrl, DmButtonTitle}) ба шакли душохаи Фазаи 11 ({OnMatch, OnNotFollowing}) —
            // бе ин, rule-ҳои қаблан сохташуда (масалан "Faridun" дар production) дар аввалин
            // иҷрои баъдӣ бо NullReferenceException вайрон мешуданд (OnMatch дар JSON нест).
            // Санҷиши "? 'OnMatch'" ин UPDATE-ро idempotent мекунад — агар такрор иҷро шавад,
            // rule-ҳои аллакай табдилёфта дубора гулбанд намешаванд.
            migrationBuilder.Sql(
                """
                UPDATE automation_rules
                SET action_config_json = jsonb_build_object('OnMatch', action_config_json::jsonb, 'OnNotFollowing', NULL)
                WHERE NOT (action_config_json::jsonb ? 'OnMatch');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follow_check_result",
                table: "automation_runs");
        }
    }
}
