#!/usr/bin/env bash
# db/create-migration.sh — EF Core diff → golang-migrate SQL draft generator
#
# Usage:
#   bash db/create-migration.sh <MigrationName>
#
# Design principle (Zero DB, Zero State File):
#   - No DB connection required at any step
#   - No local state file (.last-ef-migration etc.)
#   - Source of truth = db/migrations/ + EF Snapshot
#
# Why "0" works as FROM:
#   ef migrations add rebuilds the project. Since all previous migration .cs files
#   are deleted (only Snapshot remains), the rebuilt DLL contains exactly ONE
#   migration (the newly added one). Therefore:
#     `script 0 NewMig` = only NewMig's Up() SQL   ✅
#     `script NewMig 0` = only NewMig's Down() SQL  ✅

set -euo pipefail

MIGRATION_NAME="${1:?Usage: bash db/create-migration.sh <MigrationName>}"
PROJECT="Infrastructure"
MIGRATIONS_DIR="db/migrations"

# ── Auto version number (from existing golang-migrate files) ──────────────────
LAST_NUM=$(ls "$MIGRATIONS_DIR"/*.up.sql 2>/dev/null \
  | sed 's|.*[/\\]\([0-9]*\)_.*|\1|' | sort -rn | head -1 || echo "0")
NEXT_NUM=$(printf "%06d" $(( ${LAST_NUM:-0} + 1 )))

UP_FILE="$MIGRATIONS_DIR/${NEXT_NUM}_${MIGRATION_NAME}.up.sql"
DOWN_FILE="$MIGRATIONS_DIR/${NEXT_NUM}_${MIGRATION_NAME}.down.sql"

# ── Add EF migration (rebuilds project, updates Snapshot) ─────────────────────
echo "==> Adding EF migration: $MIGRATION_NAME"
dotnet ef migrations add "$MIGRATION_NAME" --project "$PROJECT"

EF_CS_FILE=$(find "$PROJECT/Migrations" -name "*_${MIGRATION_NAME}.cs" | head -1)
if [ -z "$EF_CS_FILE" ]; then
  echo "ERROR: Migration .cs not found — model may have no changes." >&2
  exit 1
fi
EF_FULL_NAME=$(basename "$EF_CS_FILE" .cs)
echo "==> EF migration name: $EF_FULL_NAME"

# ── SQL cleaner ───────────────────────────────────────────────────────────────
# Removes EF-specific metadata that conflicts with golang-migrate:
#   - START TRANSACTION / COMMIT  → golang-migrate manages transactions itself
#   - INSERT INTO "__EFMigrationsHistory" ... VALUES (...);
#     → EF's migration tracking table; golang-migrate uses schema_migrations instead
#     → Uses awk to consume both the INSERT line AND the following VALUES line
clean_sql() {
  awk '
    /__EFMigrationsHistory/ { skip=1; next }
    skip && /;/ { skip=0; next }
    skip { next }
    /^START TRANSACTION/ { next }
    /^COMMIT/ { next }
    { print }
  ' | sed '/^[[:space:]]*$/d'
}

# ── Generate UP / DOWN SQL ────────────────────────────────────────────────────
# FROM=0 is safe because the rebuilt DLL contains only this one migration.
echo "==> Generating UP SQL  → $UP_FILE"
dotnet ef migrations script "0" "$EF_FULL_NAME" --project "$PROJECT" --no-build \
  | clean_sql > "$UP_FILE"

echo "==> Generating DOWN SQL → $DOWN_FILE"
dotnet ef migrations script "$EF_FULL_NAME" "0" --project "$PROJECT" --no-build \
  | clean_sql > "$DOWN_FILE"

# ── Delete EF .cs files (Snapshot stays as diff baseline) ────────────────────
echo "==> Removing EF migration .cs files..."
rm -f "$PROJECT/Migrations/${EF_FULL_NAME}.cs" \
      "$PROJECT/Migrations/${EF_FULL_NAME}.Designer.cs"

echo ""
echo "✅ Draft generated:"
echo "   $UP_FILE"
echo "   $DOWN_FILE"
echo ""
echo "⚠️  Review before committing:"
echo "   - Add missing FK constraints / indexes (EF omits some)"
echo "   - Verify down.sql correctness"
