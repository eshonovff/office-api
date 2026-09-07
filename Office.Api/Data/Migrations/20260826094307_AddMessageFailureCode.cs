using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageFailureCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_code",
                table: "messages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Backfill — як UPDATE-и якхела бар рӯи ҳама сатр (на C#/EF даргирад ҳар сатрро
            // алоҳида кашад ва аз нав нависад — он бо ҳазорон сатр суст мешавад ва аз хотира
            // мегузарад). Функсияи муваққатии PL/pgSQL истисноро мегирад (JSON-и вайрон ё
            // сохтори ношинос дар ҲАР сатр то ҳол намебояд тамоми UPDATE-ро вайрон кунад — ҳамон
            // қоидаи "ҳеҷ гоҳ истисно" мисли MetaErrorCodeExtractor-и C#).
            //
            // Манбаъ: COALESCE(failure_detail, failure_reason) — то 2026-08-25 failure_detail
            // вуҷуд надошт, ва failure_reason барои GraphApiException шакли "{Provider} Graph
            // API хатогӣ: {json-и хом}" дошт (пешванд пеш аз JSON). Функсия аввалин "{"-ро
            // меёбад — ҳамон мантиқи MetaErrorCodeExtractor.Extract дар C#.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.extract_failure_code(provider_prefix text, raw_text text)
                RETURNS text AS $$
                DECLARE
                    json_start int;
                    parsed jsonb;
                    err_code text;
                    err_subcode text;
                BEGIN
                    IF provider_prefix IS NULL OR raw_text IS NULL THEN
                        RETURN NULL;
                    END IF;

                    json_start := position('{' in raw_text);
                    IF json_start = 0 THEN
                        RETURN NULL;
                    END IF;

                    BEGIN
                        parsed := substring(raw_text from json_start)::jsonb;
                    EXCEPTION WHEN OTHERS THEN
                        RETURN NULL;
                    END;

                    IF jsonb_typeof(parsed -> 'error') IS DISTINCT FROM 'object' THEN
                        RETURN NULL;
                    END IF;

                    err_code := parsed -> 'error' ->> 'code';
                    IF err_code IS NULL OR err_code !~ '^-?[0-9]+$' THEN
                        RETURN NULL;
                    END IF;

                    err_subcode := parsed -> 'error' ->> 'error_subcode';
                    IF err_subcode IS NOT NULL AND err_subcode !~ '^-?[0-9]+$' THEN
                        err_subcode := NULL;
                    END IF;

                    IF err_subcode IS NULL THEN
                        RETURN provider_prefix || '_' || err_code;
                    ELSE
                        RETURN provider_prefix || '_' || err_code || '_' || err_subcode;
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                UPDATE messages m
                SET failure_code = pg_temp.extract_failure_code(
                    CASE ch.type WHEN 'WhatsApp' THEN 'WA' WHEN 'Instagram' THEN 'IG' WHEN 'Facebook' THEN 'FB' ELSE NULL END,
                    COALESCE(m.failure_detail, m.failure_reason))
                FROM conversations co
                JOIN channels ch ON ch.id = co.channel_id
                WHERE m.conversation_id = co.id
                  AND m.delivery_status = 'Failed'
                  AND COALESCE(m.failure_detail, m.failure_reason) IS NOT NULL;

                DROP FUNCTION pg_temp.extract_failure_code(text, text);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_messages_failure_code",
                table: "messages",
                column: "failure_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_failure_code",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "failure_code",
                table: "messages");
        }
    }
}
